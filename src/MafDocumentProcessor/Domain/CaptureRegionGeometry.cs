namespace MafDocumentProcessor.Domain;

public enum CaptureRegionGeometryRejection
{
    None,
    InvalidBounds,
    BelowMinimumSize,
    EmptyPixelCrop
}

public readonly record struct PixelRectangle(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public static class CaptureRegionGeometry
{
    public static bool TryCreateTrustedBounds(
        ProposedNormalizedBounds proposed,
        double minWidth,
        double minHeight,
        double minArea,
        out NormalizedBounds? bounds,
        out CaptureRegionGeometryRejection rejection)
    {
        bounds = null;
        if (!IsFinite(proposed.X)
            || !IsFinite(proposed.Y)
            || !IsFinite(proposed.Width)
            || !IsFinite(proposed.Height)
            || proposed.Width <= 0
            || proposed.Height <= 0
            || proposed.X < 0
            || proposed.Y < 0
            || proposed.X + proposed.Width > 1
            || proposed.Y + proposed.Height > 1)
        {
            rejection = CaptureRegionGeometryRejection.InvalidBounds;
            return false;
        }

        if (proposed.Width < minWidth
            || proposed.Height < minHeight
            || proposed.Width * proposed.Height < minArea)
        {
            rejection = CaptureRegionGeometryRejection.BelowMinimumSize;
            return false;
        }

        bounds = new NormalizedBounds(proposed.X, proposed.Y, proposed.Width, proposed.Height);
        rejection = CaptureRegionGeometryRejection.None;
        return true;
    }

    public static bool TryCreateTrustedOutline(
        IReadOnlyList<ProposedNormalizedPoint>? outline,
        out IReadOnlyList<NormalizedPoint>? trusted)
    {
        trusted = null;
        if (outline is null)
        {
            return true;
        }

        if (outline.Count != 4)
        {
            return false;
        }

        var points = new NormalizedPoint[4];
        for (var index = 0; index < outline.Count; index++)
        {
            var point = outline[index];
            if (!IsUnitInterval(point.X) || !IsUnitInterval(point.Y))
            {
                return false;
            }

            points[index] = new NormalizedPoint(point.X, point.Y);
        }

        trusted = points;
        return true;
    }

    public static decimal? TrustConfidence(decimal? confidence)
    {
        return confidence is >= 0 and <= 1 ? confidence : null;
    }

    public static NormalizedBounds Expand(NormalizedBounds bounds, double edgePadding)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!double.IsFinite(edgePadding) || edgePadding < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgePadding),
                edgePadding,
                "Edge padding must be finite and not negative.");
        }

        if (edgePadding == 0)
        {
            return bounds;
        }

        var x = Math.Max(0, bounds.X - edgePadding);
        var y = Math.Max(0, bounds.Y - edgePadding);
        var right = Math.Min(1, bounds.X + bounds.Width + edgePadding);
        var bottom = Math.Min(1, bounds.Y + bounds.Height + edgePadding);
        return new NormalizedBounds(x, y, right - x, bottom - y);
    }

    public static PixelRectangle MapToPixels(NormalizedBounds bounds, int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);

        var left = ToPixel(bounds.X, imageWidth);
        var top = ToPixel(bounds.Y, imageHeight);
        var right = ToPixel(bounds.X + bounds.Width, imageWidth);
        var bottom = ToPixel(bounds.Y + bounds.Height, imageHeight);

        left = Math.Clamp(left, 0, imageWidth);
        right = Math.Clamp(right, left, imageWidth);
        top = Math.Clamp(top, 0, imageHeight);
        bottom = Math.Clamp(bottom, top, imageHeight);

        return new PixelRectangle(left, top, right - left, bottom - top);
    }

    public static double IntersectionOverUnion(NormalizedBounds left, NormalizedBounds right)
    {
        var intersectionLeft = Math.Max(left.X, right.X);
        var intersectionTop = Math.Max(left.Y, right.Y);
        var intersectionRight = Math.Min(left.X + left.Width, right.X + right.Width);
        var intersectionBottom = Math.Min(left.Y + left.Height, right.Y + right.Height);
        var width = intersectionRight - intersectionLeft;
        var height = intersectionBottom - intersectionTop;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var intersection = width * height;
        var union = left.Area + right.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static int ToPixel(double normalized, int dimension)
    {
        return (int)Math.Round(normalized * dimension, MidpointRounding.AwayFromZero);
    }

    private static bool IsFinite(double value)
    {
        return double.IsFinite(value);
    }

    private static bool IsUnitInterval(double value)
    {
        return double.IsFinite(value) && value >= 0 && value <= 1;
    }
}

using System.Text.Json.Serialization;

namespace MafDocumentProcessor.Domain;

public sealed record NormalizedPoint
{
    public NormalizedPoint(double x, double y)
    {
        X = RequireCoordinate(x, nameof(x));
        Y = RequireCoordinate(y, nameof(y));
    }

    public double X { get; }

    public double Y { get; }

    private static double RequireCoordinate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A normalized coordinate must be finite and between zero and one.");
        }

        return value;
    }
}

public sealed record NormalizedBounds
{
    public NormalizedBounds(double x, double y, double width, double height)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(width, nameof(width));
        RequireFinite(height, nameof(height));

        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Normalized bounds must have positive size and remain inside the zero-to-one image space.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    [JsonIgnore]
    public double Area => Width * Height;

    private static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A normalized bound must be finite.");
        }
    }
}

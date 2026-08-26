using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MafDocumentProcessor.Domain;

public sealed record CaptureSourceImageMetadata(
    int OriginalWidthPixels,
    int OriginalHeightPixels,
    int OrientedWidthPixels,
    int OrientedHeightPixels)
{
    public static CaptureSourceImageMetadata From(OrientedCaptureSourceImage source)
    {
        return new CaptureSourceImageMetadata(
            source.OriginalWidthPixels,
            source.OriginalHeightPixels,
            source.WidthPixels,
            source.HeightPixels);
    }
}

public sealed class OrientedCaptureSourceImage : IDisposable
{
    private Image<Rgba32>? _image;

    internal OrientedCaptureSourceImage(
        CompositeCaptureSource source,
        Image<Rgba32> image,
        int originalWidthPixels,
        int originalHeightPixels)
    {
        Source = source;
        _image = image;
        OriginalWidthPixels = originalWidthPixels;
        OriginalHeightPixels = originalHeightPixels;
        WidthPixels = image.Width;
        HeightPixels = image.Height;
    }

    public CompositeCaptureSource Source { get; }

    public int OriginalWidthPixels { get; }

    public int OriginalHeightPixels { get; }

    public int WidthPixels { get; }

    public int HeightPixels { get; }

    internal Image<Rgba32> CloneImage()
    {
        return (_image ?? throw new ObjectDisposedException(nameof(OrientedCaptureSourceImage))).Clone();
    }

    internal Image<Rgba32> CloneCrop(PixelRectangle bounds)
    {
        var image = _image ?? throw new ObjectDisposedException(nameof(OrientedCaptureSourceImage));
        if (bounds.IsEmpty
            || bounds.X < 0
            || bounds.Y < 0
            || bounds.X + bounds.Width > image.Width
            || bounds.Y + bounds.Height > image.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds,
                "The crop must lie inside the oriented source image.");
        }

        return image.Clone(context => context.Crop(new Rectangle(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height)));
    }

    public void Dispose()
    {
        _image?.Dispose();
        _image = null;
    }
}

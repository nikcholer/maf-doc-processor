using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MafDocumentProcessor.Services;

public sealed class CaptureSourceImageDecoder(CompositeCaptureOptions options)
    : ICaptureSourceImageDecoder
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png"
    };

    public OrientedCaptureSourceImage Decode(CompositeCaptureSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var request = source.Request;
        ValidateDeclaredSource(request);

        try
        {
            var imageInfo = Image.Identify(request.Content)
                ?? throw new CaptureSourceValidationException(
                    $"Source '{source.SourceItemId}' could not be identified as an image.");
            ValidateDecodedFormat(
                source.SourceItemId,
                request,
                imageInfo.Metadata.DecodedImageFormat?.DefaultMimeType);
            ValidateDimensions(source.SourceItemId, imageInfo.Width, imageInfo.Height);

            var image = Image.Load<Rgba32>(request.Content);
            try
            {
                var originalWidth = image.Width;
                var originalHeight = image.Height;
                image.Mutate(operation => operation.AutoOrient());
                ValidateDimensions(source.SourceItemId, image.Width, image.Height);

                return new OrientedCaptureSourceImage(
                    source,
                    image,
                    originalWidth,
                    originalHeight);
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
        catch (CaptureSourceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new CaptureSourceValidationException(
                $"Source '{source.SourceItemId}' could not be decoded as a supported PNG or JPEG image.",
                ex);
        }
    }

    private void ValidateDeclaredSource(FileRequest request)
    {
        if (request.Content.Length == 0)
        {
            throw new CaptureSourceValidationException("The source image is empty.");
        }

        if (request.Content.LongLength > options.MaxSourceBytes)
        {
            throw new CaptureSourceValidationException(
                $"The source image must be {options.MaxSourceBytes} bytes or smaller.");
        }

        if (!AllowedContentTypes.Contains(request.ContentType))
        {
            throw new CaptureSourceValidationException(
                $"Unsupported source content type '{request.ContentType}'. Use image/png or image/jpeg.");
        }

        var extension = Path.GetExtension(request.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new CaptureSourceValidationException(
                $"Unsupported source file extension '{extension}'. Use .png, .jpg, or .jpeg.");
        }
    }

    private static void ValidateDecodedFormat(
        string sourceItemId,
        FileRequest request,
        string? decodedContentType)
    {
        if (decodedContentType is null || !AllowedContentTypes.Contains(decodedContentType))
        {
            throw new CaptureSourceValidationException(
                $"Source '{sourceItemId}' did not contain a supported PNG or JPEG image.");
        }

        var extension = Path.GetExtension(request.FileName);
        var extensionMatches = string.Equals(decodedContentType, "image/png", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            : string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(decodedContentType, request.ContentType, StringComparison.OrdinalIgnoreCase)
            || !extensionMatches)
        {
            throw new CaptureSourceValidationException(
                $"Source '{sourceItemId}' content does not match its declared type and file extension.");
        }
    }

    private void ValidateDimensions(string sourceItemId, int width, int height)
    {
        if (width <= 0 || height <= 0
            || width > options.MaxSourceWidthPixels
            || height > options.MaxSourceHeightPixels
            || (long)width * height > options.MaxSourcePixelCount)
        {
            throw new CaptureSourceValidationException(
                $"Source '{sourceItemId}' dimensions {width}x{height} exceed the configured capture limits.");
        }
    }
}

using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MafDocumentProcessor.Services;

public sealed class CaptureDetectionImagePreparer(ModelImagePreprocessingSettings settings)
    : ICaptureDetectionImagePreparer
{
    public async ValueTask<FileRequest> PrepareAsync(
        OrientedCaptureSourceImage source,
        CancellationToken cancellationToken)
    {
        using var image = source.CloneImage();
        var targetLongEdge = settings.RegionDetectionMaxLongEdgePixels;
        var resizeScale = settings.Enabled && targetLongEdge > 0
            ? Math.Min(1d, targetLongEdge / (double)Math.Max(image.Width, image.Height))
            : 1d;

        if (resizeScale < 1d)
        {
            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Size = new Size(
                    Math.Max(1, (int)Math.Round(image.Width * resizeScale)),
                    Math.Max(1, (int)Math.Round(image.Height * resizeScale))),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));
        }

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder
        {
            Quality = Math.Clamp(settings.JpegQuality, 1, 100)
        }, cancellationToken);
        var content = output.ToArray();
        var originalRequest = source.Source.Request;

        return originalRequest with
        {
            Content = content,
            FileName = $"{Path.GetFileNameWithoutExtension(originalRequest.FileName)}.model-region-detection.jpg",
            ContentType = "image/jpeg",
            FileSizeBytes = content.LongLength
        };
    }
}

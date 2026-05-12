using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MafDocumentProcessor.Services;

public sealed class ModelImagePreprocessor(
    ModelImagePreprocessingSettings settings,
    ILogger<ModelImagePreprocessor>? logger = null) : IModelImagePreprocessor
{
    private readonly ILogger<ModelImagePreprocessor> _logger =
        logger ?? NullLogger<ModelImagePreprocessor>.Instance;

    public static IModelImagePreprocessor CreateDefault()
    {
        return new ModelImagePreprocessor(new ModelImagePreprocessingSettings());
    }

    public async ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
        FileRequest request,
        ModelImagePreprocessingPurpose purpose,
        CancellationToken cancellationToken)
    {
        using var image = Image.Load(request.Content);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        image.Mutate(operation => operation.AutoOrient());

        var targetLongEdge = GetTargetLongEdge(purpose);
        var resizeScale = settings.Enabled && targetLongEdge > 0
            ? Math.Min(1d, targetLongEdge / (double)Math.Max(image.Width, image.Height))
            : 1d;

        if (resizeScale >= 1d)
        {
            LogResult(
                purpose,
                resized: false,
                originalWidth,
                originalHeight,
                image.Width,
                image.Height,
                request.FileSizeBytes,
                request.FileSizeBytes);

            return new ModelImagePreprocessingResult(
                request,
                purpose,
                WasResized: false,
                originalWidth,
                originalHeight,
                image.Width,
                image.Height,
                request.FileSizeBytes,
                request.FileSizeBytes);
        }

        var targetWidth = Math.Max(1, (int)Math.Round(image.Width * resizeScale));
        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * resizeScale));
        image.Mutate(operation => operation.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder
        {
            Quality = Math.Clamp(settings.JpegQuality, 1, 100)
        }, cancellationToken);

        var content = output.ToArray();
        var modelRequest = request with
        {
            Content = content,
            FileName = BuildDerivedFileName(request.FileName, purpose),
            ContentType = "image/jpeg",
            FileSizeBytes = content.LongLength
        };

        LogResult(
            purpose,
            resized: true,
            originalWidth,
            originalHeight,
            image.Width,
            image.Height,
            request.FileSizeBytes,
            modelRequest.FileSizeBytes);

        return new ModelImagePreprocessingResult(
            modelRequest,
            purpose,
            WasResized: true,
            originalWidth,
            originalHeight,
            image.Width,
            image.Height,
            request.FileSizeBytes,
            modelRequest.FileSizeBytes);
    }

    private int GetTargetLongEdge(ModelImagePreprocessingPurpose purpose)
    {
        return purpose switch
        {
            ModelImagePreprocessingPurpose.Classification => settings.ClassificationMaxLongEdgePixels,
            ModelImagePreprocessingPurpose.Extraction => settings.ExtractionMaxLongEdgePixels,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
    }

    private static string BuildDerivedFileName(
        string fileName,
        ModelImagePreprocessingPurpose purpose)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return $"{stem}.model-{purpose.ToString().ToLowerInvariant()}.jpg";
    }

    private void LogResult(
        ModelImagePreprocessingPurpose purpose,
        bool resized,
        int originalWidth,
        int originalHeight,
        int width,
        int height,
        long originalBytes,
        long bytes)
    {
        _logger.LogInformation(
            "Prepared {Purpose} model image. Resized={WasResized}. Original={OriginalWidth}x{OriginalHeight}, {OriginalBytes} bytes. Model={Width}x{Height}, {Bytes} bytes.",
            purpose,
            resized,
            originalWidth,
            originalHeight,
            originalBytes,
            width,
            height,
            bytes);
    }
}

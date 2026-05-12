using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class ModelImagePreprocessorTests
{
    [Fact]
    public async Task PreprocessAsync_DownscalesLargeImageForClassification()
    {
        var request = CreateJpegRequest(400, 200);
        var preprocessor = new ModelImagePreprocessor(new ModelImagePreprocessingSettings(
            Enabled: true,
            ClassificationMaxLongEdgePixels: 100,
            ExtractionMaxLongEdgePixels: 200,
            JpegQuality: 85));

        var result = await preprocessor.PreprocessAsync(
            request,
            ModelImagePreprocessingPurpose.Classification,
            CancellationToken.None);

        Assert.True(result.WasResized);
        Assert.Equal(400, result.OriginalWidth);
        Assert.Equal(200, result.OriginalHeight);
        Assert.Equal(100, Math.Max(result.Width, result.Height));
        Assert.Equal("image/jpeg", result.Request.ContentType);
        Assert.EndsWith(".model-classification.jpg", result.Request.FileName);
        Assert.Equal(result.Request.Content.LongLength, result.Request.FileSizeBytes);
    }

    [Fact]
    public async Task PreprocessAsync_ReturnsOriginalImageWhenAlreadyWithinLimit()
    {
        var request = CreateJpegRequest(80, 40);
        var preprocessor = new ModelImagePreprocessor(new ModelImagePreprocessingSettings(
            Enabled: true,
            ClassificationMaxLongEdgePixels: 100,
            ExtractionMaxLongEdgePixels: 200,
            JpegQuality: 85));

        var result = await preprocessor.PreprocessAsync(
            request,
            ModelImagePreprocessingPurpose.Classification,
            CancellationToken.None);

        Assert.False(result.WasResized);
        Assert.Same(request, result.Request);
        Assert.Equal(80, result.Width);
        Assert.Equal(40, result.Height);
    }

    private static FileRequest CreateJpegRequest(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 90 });
        var content = output.ToArray();

        return new FileRequest(
            content,
            "test.jpg",
            "image/jpeg",
            content.LongLength,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            SourceId: "unit-test");
    }
}

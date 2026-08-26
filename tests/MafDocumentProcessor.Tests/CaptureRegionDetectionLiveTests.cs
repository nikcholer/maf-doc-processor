using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureRegionDetectionLiveTests
{
    private const string RunLiveTestsEnvironmentVariable =
        "MAF_RUN_LIVE_CAPTURE_DETECTION_TESTS";

    [Fact]
    public async Task DetectAsync_CanLocateDocumentsInTheNaturalDeskSample()
    {
        if (Environment.GetEnvironmentVariable(RunLiveTestsEnvironmentVariable) != "1")
        {
            return;
        }

        var settings = AiModelSettingsDefaults.CreateTogetherDefaults();
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                settings.DocumentRegionDetection.ApiKeyEnvironmentVariable)))
        {
            throw new InvalidOperationException(
                $"Set {settings.DocumentRegionDetection.ApiKeyEnvironmentVariable} to run live capture detection tests.");
        }

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "next-scenario-samples",
            "sources",
            "natural-desk-three-documents.png");
        var content = await File.ReadAllBytesAsync(path);
        var request = new FileRequest(
            content,
            Path.GetFileName(path),
            "image/png",
            content.LongLength,
            DateTimeOffset.UtcNow,
            "live-capture-detection");
        var source = new CompositeCaptureSource("source-001", 1, request);
        var captureOptions = new CompositeCaptureOptions();
        var decoder = new CaptureSourceImageDecoder(captureOptions);
        using var orientedSource = decoder.Decode(source);
        var detector = new ModelDocumentRegionDetector(
            new OpenAICompatibleModelChatClient(),
            new CaptureDetectionImagePreparer(new ModelImagePreprocessingSettings()),
            settings.DocumentRegionDetection,
            captureOptions);

        var result = await detector.DetectAsync(orientedSource, CancellationToken.None);

        Assert.Equal(3, result.Value.Count);
        Assert.All(result.Value, proposal =>
        {
            Assert.Equal(source.SourceItemId, proposal.SourceItemId);
            Assert.True(double.IsFinite(proposal.Bounds.X) && proposal.Bounds.X >= 0);
            Assert.True(double.IsFinite(proposal.Bounds.Y) && proposal.Bounds.Y >= 0);
            Assert.True(double.IsFinite(proposal.Bounds.Width) && proposal.Bounds.Width > 0);
            Assert.True(double.IsFinite(proposal.Bounds.Height) && proposal.Bounds.Height > 0);
            Assert.True(proposal.Bounds.X + proposal.Bounds.Width <= 1);
            Assert.True(proposal.Bounds.Y + proposal.Bounds.Height <= 1);
        });
        Assert.Equal(ModelDocumentRegionDetector.Operation, result.Usage.Operation);
        Assert.Equal(settings.DocumentRegionDetection.ModelId, result.Usage.ModelId);
        Assert.True(result.Usage.TotalTokens > 0);
    }
}

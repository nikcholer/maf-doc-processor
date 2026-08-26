using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using SixLabors.ImageSharp;
using Xunit.Abstractions;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureLocalSampleTests(ITestOutputHelper output)
{
    public const string RunLocalSamplesEnvironmentVariable = "MAF_RUN_LOCAL_CAPTURE_SAMPLES";

    [Fact]
    public async Task DetectAndCrop_CanExerciseGitignoredLocalSamples()
    {
        if (Environment.GetEnvironmentVariable(RunLocalSamplesEnvironmentVariable) != "1")
        {
            return;
        }

        var samples = DiscoverLocalSamples();
        if (samples.Count == 0)
        {
            output.WriteLine(
                "No local capture samples found under tests/MafDocumentProcessor.Tests/assets/local.");
            return;
        }

        var settings = AiModelSettingsDefaults.CreateTogetherDefaults();
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                settings.DocumentRegionDetection.ApiKeyEnvironmentVariable)))
        {
            throw new InvalidOperationException(
                $"Set {settings.DocumentRegionDetection.ApiKeyEnvironmentVariable} to run local capture samples.");
        }

        var captureOptions = new CompositeCaptureOptions();
        var decoder = new CaptureSourceImageDecoder(captureOptions);
        var detector = new ModelDocumentRegionDetector(
            new OpenAICompatibleModelChatClient(),
            new CaptureDetectionImagePreparer(new ModelImagePreprocessingSettings()),
            settings.DocumentRegionDetection,
            captureOptions);
        var validator = new CaptureRegionValidationService(captureOptions);

        foreach (var samplePath in samples)
        {
            await ExerciseSampleAsync(
                samplePath,
                decoder,
                detector,
                validator,
                CancellationToken.None);
        }
    }

    private async Task ExerciseSampleAsync(
        string samplePath,
        ICaptureSourceImageDecoder decoder,
        IDocumentRegionDetector detector,
        ICaptureRegionValidationService validator,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllBytesAsync(samplePath, cancellationToken);
        var fileName = Path.GetFileName(samplePath);
        var source = new CompositeCaptureSource(
            "source-001",
            1,
            new FileRequest(
                content,
                fileName,
                GetContentType(fileName)
                    ?? throw new InvalidOperationException($"Unsupported local sample '{fileName}'."),
                content.LongLength,
                DateTimeOffset.UtcNow,
                "local-capture-sample"));
        using var detection = await DetectAsync(decoder, detector, source, cancellationToken);
        var validation = await validator.ValidateAsync(
            new CaptureRegionValidationInput(
                new CaptureWorkflowContext("trace-local", "capture-local", "local-capture-sample"),
                source,
                detection),
            cancellationToken);

        output.WriteLine(
            "{0}: {1}x{2} oriented, {3} proposals, {4} accepted, {5} rejected.",
            fileName,
            detection.ImageMetadata?.OrientedWidthPixels,
            detection.ImageMetadata?.OrientedHeightPixels,
            detection.Proposals.Count,
            validation.AcceptedMembers.Count,
            validation.RejectedRegions.Count);

        foreach (var proposal in detection.Proposals)
        {
            output.WriteLine(
                "  proposal {0}: x={1:0.###} y={2:0.###} w={3:0.###} h={4:0.###} confidence={5}",
                proposal.DetectionIndex,
                proposal.Bounds.X,
                proposal.Bounds.Y,
                proposal.Bounds.Width,
                proposal.Bounds.Height,
                proposal.Confidence);
        }

        foreach (var member in validation.AcceptedMembers)
        {
            using var crop = Image.Load(member.CropRequest.Content);
            output.WriteLine(
                "  member {0}: crop {1}x{2} at {3},{4}",
                member.Member.MemberId,
                crop.Width,
                crop.Height,
                member.CropPixels.X,
                member.CropPixels.Y);
            Assert.Equal(member.CropPixels.Width, crop.Width);
            Assert.Equal(member.CropPixels.Height, crop.Height);
        }

        Assert.True(detection.IsSuccess);
        Assert.Equal(ModelDocumentRegionDetector.Operation, detection.ModelUsage.Calls.Single().Operation);
        Assert.All(
            validation.AcceptedMembers,
            member => Assert.False(member.CropPixels.IsEmpty));
    }

    private static async Task<CaptureSourceDetectionOutput> DetectAsync(
        ICaptureSourceImageDecoder decoder,
        IDocumentRegionDetector detector,
        CompositeCaptureSource source,
        CancellationToken cancellationToken)
    {
        var image = decoder.Decode(source);
        try
        {
            var detection = await detector.DetectAsync(image, cancellationToken);
            return new CaptureSourceDetectionOutput(
                new CaptureWorkflowContext("trace-local", "capture-local", "local-capture-sample", source.SourceItemId),
                source,
                CaptureSourceImageMetadata.From(image),
                image,
                detection.Value,
                DocumentModelUsage.FromCalls([detection.Usage]),
                [],
                []);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<string> DiscoverLocalSamples()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        directories.Add(Path.Combine(AppContext.BaseDirectory, "assets", "local"));
        directories.Add(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "assets",
            "local")));

        return directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory)
                .Where(path => GetContentType(path) is not null))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => null
        };
    }
}

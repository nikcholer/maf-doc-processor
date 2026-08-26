using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureRegionValidationTests
{
    private static readonly string SampleRoot = Path.Combine(
        AppContext.BaseDirectory,
        "next-scenario-samples",
        "sources");

    [Fact]
    public void Geometry_MapsNormalizedBoundsToPixelsWithoutDrift()
    {
        var bounds = new NormalizedBounds(0.125, 0.041667, 0.75, 0.916667);
        var pixels = CaptureRegionGeometry.MapToPixels(bounds, 800, 1200);

        Assert.Equal(100, pixels.X);
        Assert.Equal(50, pixels.Y);
        Assert.Equal(600, pixels.Width);
        Assert.Equal(1100, pixels.Height);
        Assert.InRange(Math.Abs(pixels.X / 800d - bounds.X), 0, 0.5 / 800);
        Assert.InRange(Math.Abs(pixels.Y / 1200d - bounds.Y), 0, 0.5 / 1200);
    }

    [Fact]
    public void Geometry_RejectsInvalidAndTinyBounds()
    {
        Assert.False(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(-0.02, 0.1, 0.5, 0.7),
            0.02,
            0.02,
            0.0025,
            out _,
            out var invalid));
        Assert.Equal(CaptureRegionGeometryRejection.InvalidBounds, invalid);

        Assert.False(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.1, 0.1, 0.01, 0.01),
            0.02,
            0.02,
            0.0025,
            out _,
            out var tiny));
        Assert.Equal(CaptureRegionGeometryRejection.BelowMinimumSize, tiny);

        Assert.True(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.125, 0.041667, 0.75, 0.916667),
            0.02,
            0.02,
            0.0025,
            out var trusted,
            out var none));
        Assert.Equal(CaptureRegionGeometryRejection.None, none);
        Assert.NotNull(trusted);
    }

    [Fact]
    public void Geometry_TreatsNearDuplicatesAndPartialOverlapDifferently()
    {
        Assert.True(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.125, 0.041667, 0.75, 0.916667),
            0.02,
            0.02,
            0.0025,
            out var original,
            out _));
        Assert.True(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.127, 0.043, 0.748, 0.914),
            0.02,
            0.02,
            0.0025,
            out var duplicate,
            out _));
        Assert.True(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.171, 0.104, 0.345, 0.793),
            0.02,
            0.02,
            0.0025,
            out var left,
            out _));
        Assert.True(CaptureRegionGeometry.TryCreateTrustedBounds(
            new ProposedNormalizedBounds(0.406, 0.160, 0.351, 0.761),
            0.02,
            0.02,
            0.0025,
            out var right,
            out _));

        var duplicateIoU = CaptureRegionGeometry.IntersectionOverUnion(original!, duplicate!);
        var overlapIoU = CaptureRegionGeometry.IntersectionOverUnion(left!, right!);
        var options = new CompositeCaptureOptions();

        Assert.InRange(duplicateIoU, options.DuplicateIntersectionOverUnionThreshold, 1);
        Assert.InRange(
            overlapIoU,
            options.OverlapReviewIntersectionOverUnionThreshold,
            options.DuplicateIntersectionOverUnionThreshold);
    }

    [Fact]
    public async Task Crop_UsesOrientedSourcePixelsWithoutCoordinateDrift()
    {
        var content = CreatePng(100, 100, image =>
        {
            Fill(image, 10, 10, 40, 40, Color.Red);
        });
        using var detection = CreateSuccessfulDetection(
            CreateSource(content, "blocks.png", "image/png"),
            [CreateProposal(1, 0.1, 0.1, 0.3, 0.3)]);
        var output = await ValidateAsync(detection);

        var accepted = Assert.Single(output.AcceptedMembers);
        Assert.Equal(10, accepted.CropPixels.X);
        Assert.Equal(10, accepted.CropPixels.Y);
        Assert.Equal(30, accepted.CropPixels.Width);
        Assert.Equal(30, accepted.CropPixels.Height);
        using var crop = Image.Load<Rgba32>(accepted.CropRequest.Content);
        Assert.Equal(30, crop.Width);
        Assert.Equal(30, crop.Height);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), crop[0, 0]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), crop[29, 29]);
    }

    [Fact]
    public async Task SampleCorpus_CropsTheSingleReceiptFromItsKnownBounds()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("single-receipt.png"),
            [CreateProposal(1, 0.125, 0.041667, 0.75, 0.916667, 0.98m)]);
        var output = await ValidateAsync(detection);

        var accepted = Assert.Single(output.AcceptedMembers);
        Assert.True(output.IsSuccess);
        Assert.Empty(output.RejectedRegions);
        Assert.Equal("source-001-document-001", accepted.Member.MemberId);
        Assert.Equal(100, accepted.CropPixels.X);
        Assert.Equal(50, accepted.CropPixels.Y);
        Assert.Equal(600, accepted.CropPixels.Width);
        Assert.Equal(1100, accepted.CropPixels.Height);
        using var crop = Image.Load(accepted.CropRequest.Content);
        Assert.Equal(600, crop.Width);
        Assert.Equal(1100, crop.Height);
        Assert.Equal("single-receipt-source-001-document-001.png", accepted.CropRequest.FileName);
        Assert.Equal("image/png", accepted.CropRequest.ContentType);
        Assert.Equal(1, accepted.Member.Region.DetectionIndex);
        Assert.Equal(0.98m, accepted.Member.Region.Confidence);
    }

    [Fact]
    public async Task SampleCorpus_OrdersThreeDocumentsTopToBottomThenLeftToRight()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("natural-desk-three-documents.png"),
            [
                CreateProposal(1, 0.108, 0.203, 0.199, 0.557),
                CreateProposal(2, 0.342, 0.107, 0.215, 0.783),
                CreateProposal(3, 0.618, 0.187, 0.307, 0.648)
            ]);
        var output = await ValidateAsync(detection);

        Assert.Equal(3, output.AcceptedMembers.Count);
        Assert.Empty(output.RejectedRegions);
        Assert.Equal([2, 3, 1], output.AcceptedMembers.Select(member => member.Member.Region.DetectionIndex));
        Assert.Equal(
            ["source-001-document-001", "source-001-document-002", "source-001-document-003"],
            output.AcceptedMembers.Select(member => member.Member.MemberId));
    }

    [Fact]
    public async Task SampleCorpus_KeepsOverlappingReceiptsAndSurfacesAReviewWarning()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("overlapping-receipts.png"),
            [
                CreateProposal(1, 0.171, 0.104, 0.345, 0.793),
                CreateProposal(2, 0.406, 0.160, 0.351, 0.761)
            ]);
        var output = await ValidateAsync(detection);

        Assert.Equal(2, output.AcceptedMembers.Count);
        Assert.Empty(output.RejectedRegions);
        Assert.Equal(CaptureRegionValidationService.OverlapWarning, Assert.Single(output.Warnings));
        Assert.All(
            output.AcceptedMembers,
            member => Assert.Equal(
                CaptureRegionValidationService.OverlapWarning,
                Assert.Single(member.Member.Region.Warnings)));
        Assert.All(output.AcceptedMembers, member => Assert.False(member.CropPixels.IsEmpty));
    }

    [Fact]
    public async Task SampleCorpus_RejectsADuplicateDetectionWithoutCroppingIt()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("single-receipt.png"),
            [
                CreateProposal(1, 0.125, 0.041667, 0.75, 0.916667),
                CreateProposal(2, 0.127, 0.043, 0.748, 0.914)
            ]);
        var output = await ValidateAsync(detection);

        var accepted = Assert.Single(output.AcceptedMembers);
        var rejected = Assert.Single(output.RejectedRegions);
        Assert.True(output.IsSuccess);
        Assert.Equal(1, accepted.Member.Region.DetectionIndex);
        Assert.Equal("source-001-document-001", accepted.Member.MemberId);
        Assert.Equal(2, rejected.DetectionIndex);
        Assert.Equal(CaptureRegionValidationService.InvalidDetectedRegionCode, rejected.Error.Code);
        Assert.Contains("duplicate", rejected.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("source-001.regions[2]", rejected.Error.Target);
        Assert.NotNull(rejected.TrustedBounds);
    }

    [Fact]
    public async Task SampleCorpus_EmptyDeskHasNoUsableRegion()
    {
        using var detection = CreateSuccessfulDetection(LoadSample("empty-desk.png"), []);
        var output = await ValidateAsync(detection);

        Assert.False(output.IsSuccess);
        Assert.Empty(output.AcceptedMembers);
        Assert.Empty(output.RejectedRegions);
        var error = Assert.Single(output.Errors);
        Assert.Equal(CaptureRegionValidationService.NoUsableDocumentRegionCode, error.Code);
        Assert.Same(detection.ModelUsage, output.ModelUsage);
    }

    [Fact]
    public async Task OutOfRangeAndTinyRegions_AreRejectedWithStableTargets()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("single-receipt.png"),
            [
                CreateProposal(1, -0.02, 0.1, 0.5, 0.7),
                CreateProposal(2, 0.1, 0.1, 0.01, 0.4),
                CreateProposal(3, 0.125, 0.041667, 0.75, 0.916667)
            ]);
        var output = await ValidateAsync(detection);

        Assert.True(output.IsSuccess);
        Assert.Equal(3, Assert.Single(output.AcceptedMembers).Member.Region.DetectionIndex);
        Assert.Collection(
            output.RejectedRegions,
            invalid =>
            {
                Assert.Equal(1, invalid.DetectionIndex);
                Assert.Null(invalid.TrustedBounds);
                Assert.Contains("not inside the normalized image", invalid.Error.Message, StringComparison.Ordinal);
            },
            tiny =>
            {
                Assert.Equal(2, tiny.DetectionIndex);
                Assert.Contains("useful-region threshold", tiny.Error.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task EmptyPixelCrop_IsRejectedEvenWhenNormalizedBoundsAreTrusted()
    {
        using var detection = CreateSuccessfulDetection(
            CreateSource(CreatePng(10, 10), "tiny.png", "image/png"),
            [CreateProposal(1, 0, 0, 0.02, 0.4)]);
        var output = await ValidateAsync(detection);

        Assert.False(output.IsSuccess);
        var rejected = Assert.Single(output.RejectedRegions);
        Assert.Contains("empty pixel crop", rejected.Error.Message, StringComparison.Ordinal);
        Assert.NotNull(rejected.TrustedBounds);
    }

    [Fact]
    public async Task MemberLimit_RejectsOverflowInReadingOrder()
    {
        var options = new CompositeCaptureOptions(MaxDetectedRegionsPerSource: 1);
        using var detection = CreateSuccessfulDetection(
            CreateSource(CreatePng(100, 100), "two.png", "image/png"),
            [
                CreateProposal(1, 0.55, 0.55, 0.4, 0.4),
                CreateProposal(2, 0.05, 0.05, 0.4, 0.4)
            ]);
        var output = await ValidateAsync(detection, options);

        var accepted = Assert.Single(output.AcceptedMembers);
        var rejected = Assert.Single(output.RejectedRegions);
        Assert.Equal(2, accepted.Member.Region.DetectionIndex);
        Assert.Equal(1, rejected.DetectionIndex);
        Assert.Contains("member limit", rejected.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidOutlineAndConfidence_AreDroppedWithoutRejectingTheRegion()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("single-receipt.png"),
            [
                new DocumentRegionProposal(
                    "source-001",
                    1,
                    new ProposedNormalizedBounds(0.125, 0.041667, 0.75, 0.916667),
                    [
                        new ProposedNormalizedPoint(0.1, 0.1),
                        new ProposedNormalizedPoint(1.2, 0.1),
                        new ProposedNormalizedPoint(0.8, 0.9),
                        new ProposedNormalizedPoint(0.1, 0.9)
                    ],
                    1.4m)
            ]);
        var output = await ValidateAsync(detection);

        var accepted = Assert.Single(output.AcceptedMembers);
        Assert.Null(accepted.Member.Region.Outline);
        Assert.Null(accepted.Member.Region.Confidence);
    }

    [Fact]
    public async Task FailedDetection_IsPassedThroughWithoutEvaluatingProposals()
    {
        var source = LoadSample("single-receipt.png");
        using var detection = new CaptureSourceDetectionOutput(
            new CaptureWorkflowContext("trace-45", "capture-45", "claim-45", source.SourceItemId),
            source,
            ImageMetadata: null,
            OrientedSource: null,
            Proposals: [CreateProposal(1, 0.125, 0.041667, 0.75, 0.916667)],
            DocumentModelUsage.FromCalls([]),
            [new CaptureProcessingError("invalid_capture_source", "broken source", source.SourceItemId)],
            []);
        var output = await ValidateAsync(detection);

        Assert.False(output.IsSuccess);
        Assert.Empty(output.AcceptedMembers);
        Assert.Empty(output.RejectedRegions);
        Assert.Equal("invalid_capture_source", Assert.Single(output.Errors).Code);
    }

    [Fact]
    public async Task ValidationExecutor_EmitsACompletedEventAndDisposesTheOrientedSource()
    {
        var source = LoadSample("single-receipt.png");
        var detection = CreateSuccessfulDetection(
            source,
            [CreateProposal(1, 0.125, 0.041667, 0.75, 0.916667)]);
        var executor = new CaptureRegionValidationExecutor(new CaptureRegionValidationService(
            new CompositeCaptureOptions()));
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();

        var run = await InProcessExecution.RunAsync(
            workflow,
            new CaptureRegionValidationInput(
                new CaptureWorkflowContext("trace-45", "capture-45", "claim-45"),
                source,
                detection));
        var events = run.NewEvents.ToArray();
        var output = Assert.IsType<CaptureRegionValidationOutput>(
            Assert.Single(events.OfType<WorkflowOutputEvent>()).Data);

        Assert.True(output.IsSuccess);
        Assert.Single(output.AcceptedMembers);
        var completed = Assert.IsType<CaptureRegionValidationCompletedEvent>(
            Assert.Single(events, evt => evt.Data is CaptureRegionValidationCompletedEvent).Data);
        Assert.Equal("trace-45", completed.TraceId);
        Assert.Equal("capture-45", completed.CaptureId);
        Assert.Equal("source-001", completed.SourceItemId);
        Assert.Equal(1, completed.ProposedRegionCount);
        Assert.Equal(1, completed.AcceptedRegionCount);
        Assert.Equal(0, completed.RejectedRegionCount);
        Assert.True(completed.IsSuccess);
        Assert.Empty(completed.ErrorCodes);
    }

    [Fact]
    public async Task ValidationService_PropagatesRequestCancellation()
    {
        using var detection = CreateSuccessfulDetection(
            LoadSample("single-receipt.png"),
            [CreateProposal(1, 0.125, 0.041667, 0.75, 0.916667)]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ValidateAsync(detection, cancellationToken: cancellation.Token));
    }

    private static Task<CaptureRegionValidationOutput> ValidateAsync(
        CaptureSourceDetectionOutput detection,
        CompositeCaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var service = new CaptureRegionValidationService(options ?? new CompositeCaptureOptions());
        return service.ValidateAsync(
            new CaptureRegionValidationInput(
                new CaptureWorkflowContext("trace-45", "capture-45", "claim-45"),
                detection.Source,
                detection),
            cancellationToken).AsTask();
    }

    private static CaptureSourceDetectionOutput CreateSuccessfulDetection(
        CompositeCaptureSource source,
        IReadOnlyList<DocumentRegionProposal> proposals)
    {
        var decoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions());
        var image = decoder.Decode(source);
        return new CaptureSourceDetectionOutput(
            new CaptureWorkflowContext("trace-45", "capture-45", "claim-45", source.SourceItemId),
            source,
            CaptureSourceImageMetadata.From(image),
            image,
            proposals,
            DocumentModelUsage.FromCalls(
            [
                new ModelTokenUsage(
                    ModelDocumentRegionDetector.Operation,
                    "capture-detector",
                    10,
                    5,
                    15)
            ]),
            [],
            []);
    }

    private static CompositeCaptureSource LoadSample(string fileName)
    {
        var content = File.ReadAllBytes(Path.Combine(SampleRoot, fileName));
        var contentType = string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
        return CreateSource(content, fileName, contentType);
    }

    private static CompositeCaptureSource CreateSource(
        byte[] content,
        string fileName,
        string contentType,
        string sourceItemId = "source-001")
    {
        return new CompositeCaptureSource(
            sourceItemId,
            1,
            new FileRequest(
                content,
                fileName,
                contentType,
                content.LongLength,
                DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
                "claim-45"));
    }

    private static DocumentRegionProposal CreateProposal(
        int detectionIndex,
        double x,
        double y,
        double width,
        double height,
        decimal? confidence = 0.95m,
        string sourceItemId = "source-001")
    {
        return new DocumentRegionProposal(
            sourceItemId,
            detectionIndex,
            new ProposedNormalizedBounds(x, y, width, height),
            outline: null,
            confidence);
    }

    private static byte[] CreatePng(int width, int height, Action<Image<Rgba32>>? paint = null)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        paint?.Invoke(image);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private static void Fill(Image<Rgba32> image, int left, int top, int right, int bottom, Color color)
    {
        var pixel = color.ToPixel<Rgba32>();
        image.ProcessPixelRows(accessor =>
        {
            for (var y = top; y < bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = left; x < right; x++)
                {
                    row[x] = pixel;
                }
            }
        });
    }
}

using System.Diagnostics;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureParallelismMeasurementTests(ITestOutputHelper output)
{
    private const int SourceCount = 4;
    private const int SimulatedCallMilliseconds = 250;

    [Fact]
    public async Task BoundedParallelLanes_AreFasterThanASingleLaneForIndependentSources()
    {
        var image = CreatePng(64, 64);
        var request = CompositeCaptureRequest.Create(
            Enumerable.Range(1, SourceCount)
                .Select(index => new FileRequest(
                    image,
                    $"receipt-{index}.png",
                    "image/png",
                    image.LongLength,
                    DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
                    "parallel-harness"))
                .ToArray(),
            DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
            "parallel-harness");

        var sequential = await MeasureAsync(request, sourceLanes: 1, memberLanes: 1);
        var parallel = await MeasureAsync(request, sourceLanes: 2, memberLanes: 2);

        output.WriteLine(
            "Sequential 1/1: {0} ms, {1} members, {2} calls.",
            sequential.ElapsedMilliseconds,
            sequential.MemberCount,
            sequential.ModelCalls);
        output.WriteLine(
            "Bounded 2/2: {0} ms, {1} members, {2} calls.",
            parallel.ElapsedMilliseconds,
            parallel.MemberCount,
            parallel.ModelCalls);

        Assert.Equal(SourceCount, sequential.MemberCount);
        Assert.Equal(sequential.MemberCount, parallel.MemberCount);
        Assert.Equal(sequential.ModelCalls, parallel.ModelCalls);
        Assert.True(
            sequential.ElapsedMilliseconds >= SourceCount * SimulatedCallMilliseconds,
            "Sequential source detection should pay the full simulated call cost.");
        Assert.True(
            parallel.ElapsedMilliseconds < sequential.ElapsedMilliseconds,
            $"Bounded parallel lanes should reduce wall-clock time. sequential={sequential.ElapsedMilliseconds}ms parallel={parallel.ElapsedMilliseconds}ms");
    }

    private static async Task<Measurement> MeasureAsync(
        CompositeCaptureRequest request,
        int sourceLanes,
        int memberLanes)
    {
        var options = new CompositeCaptureOptions(
            MaxConcurrentSources: sourceLanes,
            MaxConcurrentMembers: memberLanes,
            RegionEdgePadding: 0);
        var workflow = new CompositeCaptureWorkflow(
            new CaptureSourceDetectionService(
                new CaptureSourceImageDecoder(options),
                new DelayedRegionDetector()),
            new CaptureRegionValidationService(options),
            new DelayedClassifier(),
            new ImmediateReceiptExtractor(),
            new UnusedShoppingListExtractor(),
            new ReceiptPolicyOptions(),
            options,
            ModelImagePreprocessor.CreateDefault());
        var clock = Stopwatch.StartNew();
        var result = await workflow.RunAsync(request, CancellationToken.None);
        clock.Stop();
        return new Measurement(
            clock.ElapsedMilliseconds,
            result.Members.Count,
            result.ModelUsage.Calls.Count);
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private sealed record Measurement(long ElapsedMilliseconds, int MemberCount, int ModelCalls);

    private sealed class DelayedRegionDetector : IDocumentRegionDetector
    {
        public async ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            await Task.Delay(SimulatedCallMilliseconds, cancellationToken);
            IReadOnlyList<DocumentRegionProposal> proposals =
            [
                new DocumentRegionProposal(
                    source.Source.SourceItemId,
                    1,
                    new ProposedNormalizedBounds(0.1, 0.1, 0.6, 0.6),
                    outline: null,
                    confidence: 0.95m)
            ];
            return new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                proposals,
                new ModelTokenUsage(ModelDocumentRegionDetector.Operation, "timing-detector", 4, 2, 6));
        }
    }

    private sealed class DelayedClassifier : IDocumentClassifier
    {
        public async ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(SimulatedCallMilliseconds, cancellationToken);
            return new ModelResult<DocumentClassification>(
                new DocumentClassification(DocumentCategory.Receipt, 0.91m, "timing"),
                new ModelTokenUsage("classification", "timing-classifier", 3, 1, 4));
        }
    }

    private sealed class ImmediateReceiptExtractor : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData("Timing Shop", 1.00m, new DateOnly(2026, 8, 26), "Card", "GBP"),
                new ModelTokenUsage("receipt_extraction", "timing-extractor", 5, 2, 7)));
        }
    }

    private sealed class UnusedShoppingListExtractor : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Shopping-list extraction should not run.");
        }
    }
}

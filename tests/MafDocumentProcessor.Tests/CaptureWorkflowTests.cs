using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureWorkflowTests
{
    [Fact]
    public void LaneAssignment_RoundRobinsAndCanBeEmpty()
    {
        var items = new[] { "a", "b", "c" };

        Assert.Equal(["a", "c"], CaptureLaneAssignment.ForLane(items, 0, 2));
        Assert.Equal(["b"], CaptureLaneAssignment.ForLane(items, 1, 2));
        Assert.Empty(CaptureLaneAssignment.ForLane(items, 3, 4));
    }

    [Fact]
    public void SourceTopology_HasFixedLanesAndFanIn()
    {
        var workflow = CaptureWorkflowFactory.BuildSourceWorkflow(
            new CaptureSourceDetectionService(
                new CaptureSourceImageDecoder(new CompositeCaptureOptions()),
                new StubRegionDetector()),
            new CaptureRegionValidationService(new CompositeCaptureOptions(RegionEdgePadding: 0)),
            new CompositeCaptureOptions(MaxConcurrentSources: 2, MaxConcurrentMembers: 2));
        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);

        Assert.Contains("capture-source-partitioner", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-source-lane-1", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-source-lane-2", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-source-fan-in", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberTopology_HasFixedLanesAndFanIn()
    {
        var workflow = CaptureWorkflowFactory.BuildMemberWorkflow(
            CreateDocumentWorkflow(new StubClassifier(), new StubReceiptExtractor()),
            new CompositeCaptureOptions(MaxConcurrentMembers: 2));
        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);

        Assert.Contains("capture-member-partitioner", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-member-lane-1", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-member-lane-2", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-member-fan-in", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ProcessesASingleReceiptMember()
    {
        var detector = new StubRegionDetector();
        var classifier = new StubClassifier();
        var extractor = new StubReceiptExtractor();
        var workflow = CreateCaptureWorkflow(detector, classifier, extractor);
        var request = CreateRequest(CreateSource("receipt.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Equal(CaptureProcessingStatus.Succeeded, result.Status);
        var member = Assert.Single(result.Members);
        Assert.Equal(CaptureMemberDisposition.Accepted, member.Disposition);
        Assert.Equal(DocumentCategory.Receipt, member.Result?.Category);
        Assert.Equal(1, detector.CallCount);
        Assert.Single(classifier.Files);
        Assert.Single(extractor.Files);
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == ModelDocumentRegionDetector.Operation);
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "classification");
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "receipt_extraction");
        Assert.Equal(3, result.ModelUsage.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_CountsDetectionClassificationExtractionAndRepairExactlyOnce()
    {
        var detector = new StubRegionDetector();
        var classifier = new StubClassifier();
        var extractor = new SequenceReceiptExtractor(
            new ReceiptData("", 4.50m, null, "Card", "GBP"),
            new ReceiptData("First Shop", 4.50m, null, "Card", "GBP"),
            new ReceiptData("Second Shop", 7.25m, null, "Card", "GBP"));
        var workflow = CreateCaptureWorkflow(
            detector,
            classifier,
            extractor,
            options: new CompositeCaptureOptions(
                MaxConcurrentSources: 1,
                MaxConcurrentMembers: 1,
                RegionEdgePadding: 0));
        var request = CreateRequest(
            CreateSource("first.png", CreatePng(80, 80)),
            CreateSource("second.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Equal(2, detector.CallCount);
        Assert.Equal(2, classifier.Files.Count);
        Assert.Equal(3, extractor.CallCount);
        Assert.Equal(2, result.ModelUsage.Calls.Count(call =>
            call.Operation == ModelDocumentRegionDetector.Operation));
        Assert.Equal(2, result.ModelUsage.Calls.Count(call => call.Operation == "classification"));
        Assert.Equal(3, result.ModelUsage.Calls.Count(call => call.Operation == "receipt_extraction"));
        Assert.Equal(7, result.ModelUsage.Calls.Count);
        Assert.Equal(41, result.ModelUsage.TotalTokens);
        Assert.Equal(0.000041m, result.ModelUsage.EstimatedTotalCostUsd);
        Assert.Equal(41, result.ModelUsage.TotalDurationMilliseconds);
        Assert.Equal(
            [3, 2],
            result.Members
                .Where(member => member.Result is not null)
                .Select(member => member.Result!.ModelUsage.Calls.Count)
                .ToArray());
    }

    [Fact]
    public async Task RunAsync_IsolatesAFailedSourceAndKeepsAValidSibling()
    {
        var detector = new StubRegionDetector();
        var workflow = CreateCaptureWorkflow(detector, new StubClassifier(), new StubReceiptExtractor());
        var request = CreateRequest(
            CreateSource("broken.png", [1, 2, 3]),
            CreateSource("receipt.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Equal(CaptureProcessingStatus.PartiallySucceeded, result.Status);
        Assert.Equal(CaptureProcessingStatus.Failed, result.Sources[0].Status);
        Assert.NotEmpty(result.Sources[0].Errors);
        Assert.Equal(CaptureProcessingStatus.Succeeded, result.Sources[1].Status);
        Assert.Single(result.Members, member => member.Disposition == CaptureMemberDisposition.Accepted);
        Assert.Equal(1, detector.CallCount);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedWhenNoUsableRegionExists()
    {
        var detector = new StubRegionDetector { Proposals = [] };
        var classifier = new StubClassifier();
        var workflow = CreateCaptureWorkflow(detector, classifier, new StubReceiptExtractor());
        var request = CreateRequest(CreateSource("empty.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Equal(CaptureProcessingStatus.Failed, result.Status);
        Assert.DoesNotContain(result.Members, member => member.Status == CaptureMemberStatus.Processed);
        Assert.Empty(classifier.Files);
    }

    [Fact]
    public async Task RunAsync_DoesNotClassifyARejectedDuplicate()
    {
        var detector = new StubRegionDetector
        {
            Proposals =
            [
                new ProposedNormalizedBounds(0.1, 0.1, 0.5, 0.5),
                new ProposedNormalizedBounds(0.11, 0.11, 0.5, 0.5)
            ]
        };
        var classifier = new StubClassifier();
        var workflow = CreateCaptureWorkflow(detector, classifier, new StubReceiptExtractor());
        var request = CreateRequest(CreateSource("receipt.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Single(classifier.Files);
        Assert.Contains(result.Members, member => member.Disposition == CaptureMemberDisposition.Accepted);
        Assert.Contains(
            result.Members,
            member => member.Status == CaptureMemberStatus.Failed
                && member.Error?.Code == CaptureRegionValidationService.InvalidDetectedRegionCode);
    }

    [Fact]
    public async Task RunAsync_IsolatesAMemberWorkflowFailure()
    {
        var detector = new StubRegionDetector();
        var extractor = new StubReceiptExtractor { Exception = new ModelProviderException("down", new HttpRequestException()) };
        var workflow = CreateCaptureWorkflow(detector, new StubClassifier(), extractor);
        var request = CreateRequest(CreateSource("receipt.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        var member = Assert.Single(result.Members);
        Assert.Equal(CaptureMemberStatus.Failed, member.Status);
        Assert.Equal("model_provider_failed", member.Error?.Code);
        Assert.Equal(CaptureProcessingStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RunAsync_RoutesAnUnsupportedCropWithoutCallingReceiptExtraction()
    {
        var detector = new StubRegionDetector();
        var extractor = new StubReceiptExtractor();
        var workflow = CreateCaptureWorkflow(
            detector,
            new StubClassifier { Category = DocumentCategory.Unknown, Confidence = 0.4m },
            extractor);
        var request = CreateRequest(CreateSource("ticket.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        var member = Assert.Single(result.Members);
        Assert.Equal(CaptureMemberDisposition.Rejected, member.Disposition);
        Assert.False(member.Result?.IsSuccess);
        Assert.Empty(extractor.Files);
        Assert.Equal(CaptureProcessingStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RunAsync_RoutesShoppingListAndSujikoMembers()
    {
        var shopping = new StubShoppingListExtractor { AllowCalls = true };
        var sujiko = new StubSujikoExtractor();
        var receipt = new StubReceiptExtractor();
        var shoppingWorkflow = CreateCaptureWorkflow(
            new StubRegionDetector(),
            new StubClassifier { Category = DocumentCategory.ShoppingList },
            receipt,
            shopping,
            sujiko);
        var sujikoWorkflow = CreateCaptureWorkflow(
            new StubRegionDetector(),
            new StubClassifier { Category = DocumentCategory.SujikoPuzzle },
            receipt,
            shopping,
            sujiko);
        var request = CreateRequest(CreateSource("list.png", CreatePng(80, 80)));

        var shoppingResult = await shoppingWorkflow.RunAsync(request, CancellationToken.None);
        var sujikoResult = await sujikoWorkflow.RunAsync(
            CreateRequest(CreateSource("sujiko.png", CreatePng(80, 80))),
            CancellationToken.None);

        Assert.Equal(DocumentCategory.ShoppingList, Assert.Single(shoppingResult.Members).Result?.Category);
        Assert.Equal(DocumentCategory.SujikoPuzzle, Assert.Single(sujikoResult.Members).Result?.Category);
        Assert.Empty(receipt.Files);
        Assert.Contains(shoppingResult.ModelUsage.Calls, call => call.Operation == "shopping_list_extraction");
        Assert.Contains(sujikoResult.ModelUsage.Calls, call => call.Operation == "sujiko_puzzle_extraction");
    }

    [Fact]
    public async Task RunAsync_RoutesExpenseReportMembersWithReviewDisposition()
    {
        var expense = new StubExpenseReportExtractor { AllowCalls = true };
        var receipt = new StubReceiptExtractor();
        var workflow = CreateCaptureWorkflow(
            new StubRegionDetector(),
            new StubClassifier { Category = DocumentCategory.ExpenseReport, Confidence = 0.97m },
            receipt,
            expenseReportExtractor: expense);
        var request = CreateRequest(CreateSource("expense.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        var member = Assert.Single(result.Members);
        Assert.Equal(DocumentCategory.ExpenseReport, member.Result?.Category);
        Assert.True(member.Result?.IsSuccess);
        Assert.Equal(CaptureMemberDisposition.Review, member.Disposition);
        Assert.Contains(
            member.DispositionReasons,
            reason => reason == ExpenseReportResultExecutor.AttestationPrompt);
        Assert.Empty(receipt.Files);
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "expense_report_extraction");
    }

    [Fact]
    public async Task RunAsync_EnforcesCaptureMemberLimitWithoutExtraClassification()
    {
        var detector = new StubRegionDetector
        {
            Proposals =
            [
                new ProposedNormalizedBounds(0.05, 0.05, 0.3, 0.3),
                new ProposedNormalizedBounds(0.55, 0.55, 0.3, 0.3)
            ]
        };
        var classifier = new StubClassifier();
        var workflow = CreateCaptureWorkflow(
            detector,
            classifier,
            new StubReceiptExtractor(),
            options: new CompositeCaptureOptions(
                MaxConcurrentSources: 1,
                MaxConcurrentMembers: 1,
                MaxMembersPerCapture: 1,
                RegionEdgePadding: 0));
        var request = CreateRequest(CreateSource("desk.png", CreatePng(100, 100)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Single(classifier.Files);
        Assert.Single(result.Members, member => member.Status == CaptureMemberStatus.Processed);
        Assert.Contains(
            result.Members,
            member => member.Status == CaptureMemberStatus.Failed
                && member.Error?.Message.Contains("member limit", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task RunAsync_IsolatesADetectorTimeoutToTheSource()
    {
        var detector = new StubRegionDetector { Exception = new TimeoutException("slow detector") };
        var classifier = new StubClassifier();
        var workflow = CreateCaptureWorkflow(detector, classifier, new StubReceiptExtractor());
        var request = CreateRequest(CreateSource("desk.png", CreatePng(80, 80)));

        var result = await workflow.RunAsync(request, CancellationToken.None);

        Assert.Equal(CaptureProcessingStatus.Failed, result.Status);
        Assert.Contains(result.Sources[0].Errors, error => error.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(classifier.Files);
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var detector = new CancellationObservingRegionDetector();
        var workflow = CreateCaptureWorkflow(detector, new StubClassifier(), new StubReceiptExtractor());
        var request = CreateRequest(CreateSource("receipt.png", CreatePng(80, 80)));
        var run = workflow.RunAsync(request, cancellation.Token);
        await detector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
    }

    [Fact]
    public async Task RunAsync_EmitsSourceAndMemberBoundaryEvents()
    {
        var options = new CompositeCaptureOptions(
            MaxConcurrentSources: 2,
            MaxConcurrentMembers: 2,
            RegionEdgePadding: 0);
        var workflow = CaptureWorkflowFactory.BuildSourceWorkflow(
            new CaptureSourceDetectionService(
                new CaptureSourceImageDecoder(new CompositeCaptureOptions()),
                new StubRegionDetector()),
            new CaptureRegionValidationService(new CompositeCaptureOptions(RegionEdgePadding: 0)),
            options);
        var request = CreateRequest(CreateSource("receipt.png", CreatePng(80, 80)));
        var run = await InProcessExecution.RunAsync(workflow, request);
        var events = run.NewEvents.Select(evt => evt.Data).ToArray();

        var started = Assert.Single(events.OfType<CaptureStartedEvent>());
        var sourceCompleted = Assert.Single(events.OfType<CaptureSourceCompletedEvent>());
        var sourcesAggregated = Assert.Single(events.OfType<CaptureSourcesAggregatedEvent>());
        Assert.Equal(request.TraceId, started.TraceId);
        Assert.Equal(request.CaptureId, sourceCompleted.CaptureId);
        Assert.Equal(request.SourceId, sourcesAggregated.SourceId);
        Assert.Equal("source-001", sourceCompleted.SourceItemId);

        var sourceStage = Assert.Single(events.OfType<CaptureSourceStageResult>());
        var memberWorkflow = CaptureWorkflowFactory.BuildMemberWorkflow(
            CreateDocumentWorkflow(new StubClassifier(), new StubReceiptExtractor()),
            options);
        var memberRun = await InProcessExecution.RunAsync(memberWorkflow, sourceStage);
        var memberEvents = memberRun.NewEvents.Select(evt => evt.Data).ToArray();

        var memberStarted = Assert.Single(memberEvents.OfType<CaptureMemberStartedEvent>());
        var memberCompleted = Assert.Single(memberEvents.OfType<CaptureMemberCompletedEvent>());
        var captureCompleted = Assert.Single(memberEvents.OfType<CaptureCompletedEvent>());
        Assert.Equal(request.TraceId, memberStarted.TraceId);
        Assert.Equal(request.CaptureId, memberCompleted.CaptureId);
        Assert.Equal(request.SourceId, captureCompleted.SourceId);
        Assert.Equal("source-001", memberStarted.SourceItemId);
        Assert.Equal("source-001-document-001", memberStarted.MemberId);
        Assert.Equal(memberStarted.MemberId, memberCompleted.MemberId);
    }

    [Fact]
    public void Composer_MarksOverlapAndLowDetectionConfidenceAsReview()
    {
        var region = new DetectedDocumentRegion(
            "source-001",
            1,
            new NormalizedBounds(0.1, 0.1, 0.4, 0.4),
            confidence: 0.62m,
            warnings: [CaptureRegionValidationService.OverlapWarning]);
        var member = new CaptureMember(
            "source-001",
            CaptureIdentifiers.MemberId("source-001", 1),
            1,
            1,
            region);
        var result = new DocumentProcessingResult(
            DocumentCategory.Receipt,
            DocumentMetadata.FromRequest(CreateFileRequest("receipt.png"), "model", 0.91m),
            new DocumentClassification(DocumentCategory.Receipt, 0.91m, "ok"),
            DocumentModelUsage.FromCalls([]),
            new ReceiptData("Shop", 1.00m, null, "Card", "GBP"),
            ShoppingList: null,
            SujikoPuzzle: null,
            ExpenseReport: null,
            PolicyResult: null,
            ExpensePolicy: null,
            ValidationResult.Valid,
            HumanReviewResult.NotRequired,
            IsSuccess: true,
            [],
            []);

        var composed = CaptureResultComposer.FromOutcome(
            member,
            new CaptureMemberWorkflowOutcome(
                new CaptureMemberProcessingInput(
                    new CaptureWorkflowContext("t", "c"),
                    member,
                    CreateFileRequest("receipt.png"),
                    new PixelRectangle(1, 1, 10, 10)),
                result,
                Error: null));

        Assert.Equal(CaptureMemberDisposition.Review, composed.Disposition);
        Assert.Contains(CaptureRegionValidationService.OverlapWarning, composed.DispositionReasons);
        Assert.Contains(
            composed.DispositionReasons,
            reason => reason.Contains("Detection confidence", StringComparison.Ordinal));
    }

    private static CompositeCaptureWorkflow CreateCaptureWorkflow(
        IDocumentRegionDetector detector,
        StubClassifier classifier,
        IReceiptExtractor extractor,
        StubShoppingListExtractor? shoppingListExtractor = null,
        StubSujikoExtractor? sujikoExtractor = null,
        StubExpenseReportExtractor? expenseReportExtractor = null,
        CompositeCaptureOptions? options = null)
    {
        options ??= new CompositeCaptureOptions(
            MaxConcurrentSources: 2,
            MaxConcurrentMembers: 2,
            RegionEdgePadding: 0);
        return new CompositeCaptureWorkflow(
            new CaptureSourceDetectionService(new CaptureSourceImageDecoder(options), detector),
            new CaptureRegionValidationService(options),
            classifier,
            extractor,
            shoppingListExtractor ?? new StubShoppingListExtractor(),
            new ReceiptPolicyOptions(),
            options,
            ModelImagePreprocessor.CreateDefault(),
            sujikoExtractor,
            expenseReportExtractor);
    }

    private static DocumentProcessingWorkflow CreateDocumentWorkflow(
        IDocumentClassifier classifier,
        IReceiptExtractor extractor)
    {
        return new DocumentProcessingWorkflow(
            classifier,
            extractor,
            new StubShoppingListExtractor(),
            new ReceiptPolicyOptions(),
            ModelImagePreprocessor.CreateDefault());
    }

    private static CompositeCaptureRequest CreateRequest(params CompositeCaptureSource[] sources)
    {
        return CompositeCaptureRequest.Create(
            sources.Select(source => source.Request).ToArray(),
            DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
            "capture-test",
            traceId: "api-trace-capture-test");
    }

    private static CompositeCaptureSource CreateSource(string fileName, byte[] content)
    {
        return new CompositeCaptureSource(
            "source-001",
            1,
            CreateFileRequest(fileName, content));
    }

    private static FileRequest CreateFileRequest(string fileName, byte[]? content = null)
    {
        content ??= [1, 2, 3];
        return new FileRequest(
            content,
            fileName,
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
            content.LongLength,
            DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
            "capture-test");
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private sealed class StubRegionDetector : IDocumentRegionDetector
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<ProposedNormalizedBounds> Proposals { get; init; } =
        [
            new ProposedNormalizedBounds(0.1, 0.1, 0.6, 0.6)
        ];

        public Exception? Exception { get; init; }

        public ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }
            IReadOnlyList<DocumentRegionProposal> proposals = Proposals
                .Select((bounds, index) => new DocumentRegionProposal(
                    source.Source.SourceItemId,
                    index + 1,
                    bounds,
                    outline: null,
                    confidence: 0.95m))
                .ToArray();
            return ValueTask.FromResult(new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                proposals,
                new ModelTokenUsage(
                    ModelDocumentRegionDetector.Operation,
                    "capture-detector",
                    4,
                    2,
                    6,
                    EstimatedTotalCostUsd: 0.000006m,
                    DurationMilliseconds: 6)));
        }
    }

    private sealed class CancellationObservingRegionDetector : IDocumentRegionDetector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation test should not complete.");
        }
    }

    private sealed class StubClassifier : IDocumentClassifier
    {
        public List<string> Files { get; } = [];

        public DocumentCategory Category { get; init; } = DocumentCategory.Receipt;

        public decimal Confidence { get; init; } = 0.91m;

        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Files.Add(request.FileName);
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(Category, Confidence, "stub"),
                new ModelTokenUsage(
                    "classification",
                    "stub-classifier",
                    3,
                    1,
                    4,
                    EstimatedTotalCostUsd: 0.000004m,
                    DurationMilliseconds: 4)));
        }
    }

    private sealed class StubReceiptExtractor : IReceiptExtractor
    {
        public List<string> Files { get; } = [];

        public Exception? Exception { get; init; }

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            Files.Add(request.FileName);
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData("Stub Shop", 4.50m, new DateOnly(2026, 8, 26), "Card", "GBP"),
                new ModelTokenUsage("receipt_extraction", "stub-extractor", 5, 2, 7)));
        }
    }

    private sealed class SequenceReceiptExtractor(params ReceiptData[] receipts) : IReceiptExtractor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Interlocked.Increment(ref _callCount) - 1;
            if (index >= receipts.Length)
            {
                throw new InvalidOperationException("No receipt response remains for the test.");
            }

            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipts[index],
                new ModelTokenUsage(
                    "receipt_extraction",
                    "stub-sequence",
                    5,
                    2,
                    7,
                    EstimatedTotalCostUsd: 0.000007m,
                    DurationMilliseconds: 7)));
        }
    }

    private sealed class StubShoppingListExtractor : IShoppingListExtractor
    {
        public bool AllowCalls { get; init; }

        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (!AllowCalls)
            {
                throw new InvalidOperationException("Shopping-list extraction should not run in these tests.");
            }

            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                new ShoppingListData("Weekly", [new ShoppingListItem("milk", 1, "pint", false)], null),
                new ModelTokenUsage("shopping_list_extraction", "stub-list", 5, 2, 7)));
        }
    }

    private sealed class StubExpenseReportExtractor : IExpenseReportExtractor
    {
        public bool AllowCalls { get; init; }

        public ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (!AllowCalls)
            {
                throw new InvalidOperationException("Expense-report extraction should not run in these tests.");
            }

            return ValueTask.FromResult(new ModelResult<ExpenseReportData>(
                new ExpenseReportData(
                    "ER-2026-014",
                    "EXPENSE REPORT",
                    "Alex Example",
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 20),
                    "GBP",
                    48.50m,
                    [
                        new ExpenseReportLine(new DateOnly(2026, 8, 4), "Train fare", null, 18.50m, "R-001"),
                        new ExpenseReportLine(new DateOnly(2026, 8, 12), "Client lunch", null, 30.00m, "R-002")
                    ],
                    Notes: null,
                    VisibleApprovalStatus: null),
                new ModelTokenUsage("expense_report_extraction", "stub-expense", 5, 2, 7)));
        }
    }

    private sealed class StubSujikoExtractor : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                new SujikoPuzzleData(new SujikoQuadrantTotals(21, 12, 21, 17), [new SujikoCellValue(2, 2, 1)]),
                new ModelTokenUsage("sujiko_puzzle_extraction", "stub-sujiko", 5, 2, 7)));
        }
    }
}

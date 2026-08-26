using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Tests;

public sealed class DocumentWorkflowFactoryTests
{
    public static TheoryData<DocumentCategory, string, string?> DocumentRoutes => new()
    {
        {
            DocumentCategory.Receipt,
            DocumentWorkflowFactory.ReceiptWorkflowExecutorId,
            "receipt_extraction"
        },
        {
            DocumentCategory.ShoppingList,
            DocumentWorkflowFactory.ShoppingListWorkflowExecutorId,
            "shopping_list_extraction"
        },
        {
            DocumentCategory.SujikoPuzzle,
            DocumentWorkflowFactory.SujikoPuzzleWorkflowExecutorId,
            "sujiko_puzzle_extraction"
        },
        {
            DocumentCategory.ExpenseReport,
            DocumentWorkflowFactory.ExpenseReportWorkflowExecutorId,
            "expense_report_extraction"
        },
        { DocumentCategory.Invoice, DocumentWorkflowFactory.UnsupportedDocumentExecutorId, null },
        { DocumentCategory.Unknown, DocumentWorkflowFactory.UnsupportedDocumentExecutorId, null }
    };

    [Fact]
    public void DocumentRoutes_CoverEveryDefinedCategory()
    {
        Assert.Equal(
            Enum.GetValues<DocumentCategory>().Order().ToArray(),
            DocumentRoutes.Select(route => route[0]).Cast<DocumentCategory>().Order().ToArray());
    }

    [Theory]
    [MemberData(nameof(DocumentRoutes))]
    public async Task BuildDocumentRoutingWorkflow_UsesExactlyOneDestinationAndPreservesContext(
        DocumentCategory category,
        string expectedDestination,
        string? expectedExtractionOperation)
    {
        var classifier = new TrackingDocumentClassifier(category);
        var receiptExtractor = new StubReceiptExtractor(new ReceiptData(
            "North Star Cafe",
            21.02m,
            new DateOnly(2026, 8, 20),
            "Visa",
            "GBP"));
        var shoppingListExtractor = new StubShoppingListExtractor(new ShoppingListData(
            "Weekly shopping",
            [new ShoppingListItem("milk", 2, "pints", false)],
            Notes: null));
        var sujikoPuzzleExtractor = new StubSujikoPuzzleExtractor(new SujikoPuzzleData(
            new SujikoQuadrantTotals(20, 11, 24, 23),
            [new SujikoCellValue(1, 3, 3)]));
        var expenseReportExtractor = new StubExpenseReportExtractor(CreateExpenseReport());
        var imagePreprocessor = new TrackingImagePreprocessor();
        var workflow = DocumentWorkflowFactory.BuildDocumentRoutingWorkflow(
            classifier,
            receiptExtractor,
            shoppingListExtractor,
            new ReceiptPolicyOptions(),
            imagePreprocessor,
            sujikoPuzzleExtractor,
            expenseReportExtractor);
        var request = CreateRequest(category);

        var run = await InProcessExecution.RunAsync(workflow, request);
        var events = run.NewEvents.ToArray();
        Assert.Empty(events.OfType<WorkflowErrorEvent>());
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());
        var result = Assert.IsType<DocumentProcessingResult>(output.Data);

        Assert.Equal(category, result.Category);
        Assert.Equal(request.FileName, result.Metadata.FileName);
        Assert.Equal(request.SourceId, result.Metadata.SourceId);
        Assert.Equal(0.95m, result.Metadata.ClassificationConfidence);
        Assert.Equal(expectedExtractionOperation is not null, result.IsSuccess);
        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(category is DocumentCategory.Receipt ? 1 : 0, receiptExtractor.CallCount);
        Assert.Equal(category is DocumentCategory.ShoppingList ? 1 : 0, shoppingListExtractor.CallCount);
        Assert.Equal(category is DocumentCategory.SujikoPuzzle ? 1 : 0, sujikoPuzzleExtractor.CallCount);
        Assert.Equal(category is DocumentCategory.ExpenseReport ? 1 : 0, expenseReportExtractor.CallCount);

        string[] expectedOperations = expectedExtractionOperation is null
            ? ["classification"]
            : ["classification", expectedExtractionOperation];
        Assert.Equal(
            expectedOperations,
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        Assert.Equal(
            expectedExtractionOperation is null
                ? [ModelImagePreprocessingPurpose.Classification]
                : [
                    ModelImagePreprocessingPurpose.Classification,
                    ModelImagePreprocessingPurpose.Extraction
                ],
            imagePreprocessor.Purposes);

        var classifiedEvent = Assert.Single(
            events.Select(evt => evt.Data).OfType<DocumentClassifiedEvent>());
        Assert.Equal(category, classifiedEvent.Category);
        Assert.Equal(request.FileName, classifiedEvent.FileName);
        Assert.Equal(request.SourceId, classifiedEvent.SourceId);
        Assert.Equal("factory-test-classifier", classifiedEvent.ModelId);
        Assert.Equal(0.95m, classifiedEvent.Confidence);

        var routeEvent = Assert.Single(
            events.Select(evt => evt.Data).OfType<DocumentRouteSelectedEvent>());
        Assert.Equal(category, routeEvent.Category);
        Assert.Equal(expectedDestination, routeEvent.DestinationExecutorId);
        Assert.Equal(request.FileName, routeEvent.FileName);
        Assert.Equal(request.SourceId, routeEvent.SourceId);

        var completedExecutorIds = events
            .OfType<ExecutorCompletedEvent>()
            .Select(completed => completed.ExecutorId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(DocumentClassificationExecutor.ExecutorId, completedExecutorIds);
        Assert.Equal(
            [expectedDestination],
            AllDocumentDestinations.Where(completedExecutorIds.Contains).ToArray());
    }

    [Fact]
    public void BuildDocumentRoutingWorkflow_ExposesEveryDestination()
    {
        var workflow = DocumentWorkflowFactory.BuildDocumentRoutingWorkflow(
            new TrackingDocumentClassifier(DocumentCategory.Receipt),
            new StubReceiptExtractor(new ReceiptData("Store", 1m, null, "Cash", "GBP")),
            new StubShoppingListExtractor(new ShoppingListData(
                null,
                [new ShoppingListItem("milk", null, null, null)],
                null)),
            new ReceiptPolicyOptions(),
            new TrackingImagePreprocessor(),
            new StubSujikoPuzzleExtractor(new SujikoPuzzleData(
                new SujikoQuadrantTotals(20, 11, 24, 23),
                [])),
            new StubExpenseReportExtractor(CreateExpenseReport()));

        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
        var dot = WorkflowVisualizer.ToDotString(workflow);

        Assert.Contains(DocumentClassificationExecutor.ExecutorId, mermaid, StringComparison.Ordinal);
        Assert.Contains(DocumentClassificationExecutor.ExecutorId, dot, StringComparison.Ordinal);
        foreach (var destination in AllDocumentDestinations)
        {
            Assert.Contains(destination, mermaid, StringComparison.Ordinal);
            Assert.Contains(destination, dot, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BuildReceiptWorkflow_RunsAndExposesTheCompleteGraph()
    {
        var workflow = DocumentWorkflowFactory.BuildReceiptWorkflow(
            new StubReceiptExtractor(new ReceiptData(
                "North Star Cafe",
                21.02m,
                new DateOnly(2026, 8, 20),
                "Visa",
                "GBP")),
            new ReceiptPolicyOptions());

        var (result, events) = await RunAsync(
            workflow,
            CreateClassifiedDocument(DocumentCategory.Receipt));

        Assert.True(result.IsSuccess);
        Assert.Equal(PolicyDecision.Approved, result.PolicyResult?.Decision);
        Assert.Equal("North Star Cafe", result.Receipt?.StoreName);
        Assert.Equal(
            ["classification", "receipt_extraction"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        AssertWorkflowIsInspectable(
            workflow,
            events,
            "ReceiptExtraction",
            "ReceiptValidation",
            "ReceiptValidationRepair",
            "ReceiptPolicy",
            "ReceiptResult");
    }

    [Fact]
    public async Task BuildShoppingListWorkflow_RunsAndExposesTheCompleteGraph()
    {
        var workflow = DocumentWorkflowFactory.BuildShoppingListWorkflow(
            new StubShoppingListExtractor(new ShoppingListData(
                "Weekly shopping",
                [new ShoppingListItem("milk", 2, "pints", false)],
                Notes: null)));

        var (result, events) = await RunAsync(
            workflow,
            CreateClassifiedDocument(DocumentCategory.ShoppingList));

        Assert.True(result.IsSuccess);
        Assert.Equal("milk", Assert.Single(result.ShoppingList?.Items ?? []).Name);
        Assert.Equal(
            ["classification", "shopping_list_extraction"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        AssertWorkflowIsInspectable(
            workflow,
            events,
            "ShoppingListExtraction",
            "ShoppingListValidation",
            "ShoppingListValidationRepair",
            "ShoppingListResult");
    }

    [Fact]
    public async Task BuildSujikoPuzzleWorkflow_RunsAndExposesTheCompleteGraph()
    {
        var workflow = DocumentWorkflowFactory.BuildSujikoPuzzleWorkflow(
            new StubSujikoPuzzleExtractor(new SujikoPuzzleData(
                new SujikoQuadrantTotals(20, 11, 24, 23),
                [
                    new SujikoCellValue(1, 3, 3),
                    new SujikoCellValue(3, 2, 7)
                ])));

        var (result, events) = await RunAsync(
            workflow,
            CreateClassifiedDocument(DocumentCategory.SujikoPuzzle));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.SujikoPuzzle?.QuadrantTotals.TopLeft);
        Assert.Equal(
            ["classification", "sujiko_puzzle_extraction"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        AssertWorkflowIsInspectable(
            workflow,
            events,
            "SujikoPuzzleExtraction",
            "SujikoPuzzleValidation",
            "SujikoPuzzleValidationRepair",
            "SujikoPuzzleResult");
    }

    [Fact]
    public async Task BuildExpenseReportWorkflow_RunsAndExposesTheCompleteGraph()
    {
        var workflow = DocumentWorkflowFactory.BuildExpenseReportWorkflow(
            new StubExpenseReportExtractor(CreateExpenseReport()),
            new ExpensePolicyOptions());

        var (result, events) = await RunAsync(
            workflow,
            CreateClassifiedDocument(DocumentCategory.ExpenseReport));

        Assert.True(result.IsSuccess);
        Assert.Equal("ER-2026-014", result.ExpenseReport?.ReportNumber);
        Assert.Equal(PolicyDecision.Approved, result.ExpensePolicy?.Decision);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Contains(
            result.HumanReview.Reasons,
            reason => reason == ExpenseReportResultExecutor.AttestationPrompt);
        Assert.True(result.HumanReview.RequiresUserAttestation);
        Assert.Equal(
            ["classification", "expense_report_extraction"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        AssertWorkflowIsInspectable(
            workflow,
            events,
            "ExpenseReportExtraction",
            "ExpenseReportValidation",
            "ExpenseReportValidationRepair",
            "ExpenseReportPolicy",
            "ExpenseReportResult");
    }

    private static async Task<(DocumentProcessingResult Result, WorkflowEvent[] Events)> RunAsync(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        ClassifiedDocument classifiedDocument)
    {
        var run = await InProcessExecution.RunAsync(workflow, classifiedDocument);
        var events = run.NewEvents.ToArray();
        Assert.Empty(events.OfType<WorkflowErrorEvent>());
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());
        return (Assert.IsType<DocumentProcessingResult>(output.Data), events);
    }

    private static ClassifiedDocument CreateClassifiedDocument(DocumentCategory category)
    {
        var request = CreateRequest(category);
        var classification = new DocumentClassification(
            category,
            Confidence: 0.95m,
            ConfidenceReasoning: "Factory test classification",
            DocumentTypeDescription: category.ToString());
        var classificationUsage = new ModelTokenUsage(
            "classification",
            "factory-test-classifier",
            InputTokens: 10,
            OutputTokens: 5,
            TotalTokens: 15);

        return new ClassifiedDocument(
            request,
            DocumentMetadata.FromRequest(
                request,
                classificationUsage.ModelId,
                classification.Confidence),
            classification,
            classificationUsage,
            request);
    }

    private static FileRequest CreateRequest(DocumentCategory category)
    {
        return new FileRequest(
            [1, 2, 3],
            $"{category}.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2026-08-26T08:00:00Z"),
            SourceId: $"factory:{category}");
    }

    private static void AssertWorkflowIsInspectable(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        IReadOnlyList<WorkflowEvent> events,
        params string[] executorIds)
    {
        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
        var dot = WorkflowVisualizer.ToDotString(workflow);
        var completedExecutorIds = events
            .OfType<ExecutorCompletedEvent>()
            .Select(completed => completed.ExecutorId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var executorId in executorIds)
        {
            Assert.Contains(executorId, mermaid, StringComparison.Ordinal);
            Assert.Contains(executorId, dot, StringComparison.Ordinal);
            Assert.Contains(executorId, completedExecutorIds);
        }
    }

    private sealed class StubReceiptExtractor(ReceiptData receipt) : IReceiptExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt,
                CreateExtractionUsage("receipt_extraction")));
        }
    }

    private sealed class StubShoppingListExtractor(ShoppingListData shoppingList)
        : IShoppingListExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                shoppingList,
                CreateExtractionUsage("shopping_list_extraction")));
        }
    }

    private sealed class StubExpenseReportExtractor(ExpenseReportData expenseReport)
        : IExpenseReportExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ExpenseReportData>(
                expenseReport,
                CreateExtractionUsage("expense_report_extraction")));
        }
    }

    private static ExpenseReportData CreateExpenseReport()
    {
        return new ExpenseReportData(
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
            "Synthetic valid example.",
            "Not yet submitted");
    }

    private sealed class StubSujikoPuzzleExtractor(SujikoPuzzleData puzzle)
        : ISujikoPuzzleExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                puzzle,
                CreateExtractionUsage("sujiko_puzzle_extraction")));
        }
    }

    private static ModelTokenUsage CreateExtractionUsage(string operation)
    {
        return new ModelTokenUsage(
            operation,
            "factory-test-extractor",
            InputTokens: 20,
            OutputTokens: 10,
            TotalTokens: 30);
    }

    private sealed class TrackingDocumentClassifier(DocumentCategory category)
        : IDocumentClassifier
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(
                    category,
                    Confidence: 0.95m,
                    ConfidenceReasoning: "Factory test classification",
                    DocumentTypeDescription: category.ToString()),
                new ModelTokenUsage(
                    "classification",
                    "factory-test-classifier",
                    InputTokens: 10,
                    OutputTokens: 5,
                    TotalTokens: 15)));
        }
    }

    private sealed class TrackingImagePreprocessor : IModelImagePreprocessor
    {
        public List<ModelImagePreprocessingPurpose> Purposes { get; } = [];

        public ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
            FileRequest request,
            ModelImagePreprocessingPurpose purpose,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Purposes.Add(purpose);
            return ValueTask.FromResult(new ModelImagePreprocessingResult(
                request,
                purpose,
                WasResized: false,
                OriginalWidth: 100,
                OriginalHeight: 100,
                Width: 100,
                Height: 100,
                request.FileSizeBytes,
                request.FileSizeBytes));
        }
    }

    private static readonly string[] AllDocumentDestinations =
    [
        DocumentWorkflowFactory.ReceiptWorkflowExecutorId,
        DocumentWorkflowFactory.ShoppingListWorkflowExecutorId,
        DocumentWorkflowFactory.SujikoPuzzleWorkflowExecutorId,
        DocumentWorkflowFactory.ExpenseReportWorkflowExecutorId,
        DocumentWorkflowFactory.UnsupportedDocumentExecutorId
    ];
}

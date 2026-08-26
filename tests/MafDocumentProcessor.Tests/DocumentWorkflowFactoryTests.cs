using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Tests;

public sealed class DocumentWorkflowFactoryTests
{
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
        var request = new FileRequest(
            [1, 2, 3],
            $"{category}.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2026-08-26T08:00:00Z"),
            SourceId: $"factory:{category}");
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
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt,
                CreateExtractionUsage("receipt_extraction")));
        }
    }

    private sealed class StubShoppingListExtractor(ShoppingListData shoppingList)
        : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                shoppingList,
                CreateExtractionUsage("shopping_list_extraction")));
        }
    }

    private sealed class StubSujikoPuzzleExtractor(SujikoPuzzleData puzzle)
        : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
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
}

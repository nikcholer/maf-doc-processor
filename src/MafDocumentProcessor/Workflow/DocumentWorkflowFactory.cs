using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MafDocumentProcessor.Workflow;

public static class DocumentWorkflowFactory
{
    public const string DocumentRoutingWorkflowName = "Document Processing";
    public const string ReceiptWorkflowName = "Receipt Processing";
    public const string ShoppingListWorkflowName = "Shopping List Processing";
    public const string SujikoPuzzleWorkflowName = "Sujiko Puzzle Processing";
    public const string ExpenseReportWorkflowName = "Expense Report Processing";
    public const string ReceiptWorkflowExecutorId = "receipt-workflow";
    public const string ShoppingListWorkflowExecutorId = "shopping-list-workflow";
    public const string SujikoPuzzleWorkflowExecutorId = "sujiko-workflow";
    public const string ExpenseReportWorkflowExecutorId = "expense-report-workflow";
    public const string UnsupportedDocumentExecutorId = UnsupportedDocumentResultExecutor.ExecutorId;

    public static Microsoft.Agents.AI.Workflows.Workflow BuildDocumentRoutingWorkflow(
        IDocumentClassifier classifier,
        IReceiptExtractor receiptExtractor,
        IShoppingListExtractor shoppingListExtractor,
        ReceiptPolicyOptions policyOptions,
        IModelImagePreprocessor imagePreprocessor,
        ISujikoPuzzleExtractor? sujikoPuzzleExtractor = null,
        IExpenseReportExtractor? expenseReportExtractor = null,
        ExpensePolicyOptions? expensePolicyOptions = null,
        ILogger<DocumentClassificationExecutor>? classificationLogger = null,
        CancellationToken cancellationToken = default)
    {
        var classificationExecutor = new DocumentClassificationExecutor(
            classifier,
            imagePreprocessor,
            cancellationToken,
            classificationLogger);
        var receiptWorkflow = BuildReceiptWorkflow(
                receiptExtractor,
                policyOptions,
                cancellationToken)
            .BindAsExecutor(ReceiptWorkflowExecutorId);
        var shoppingListWorkflow = BuildShoppingListWorkflow(
                shoppingListExtractor,
                cancellationToken)
            .BindAsExecutor(ShoppingListWorkflowExecutorId);
        var sujikoPuzzleWorkflow = BuildSujikoPuzzleWorkflow(
                sujikoPuzzleExtractor ?? new UnconfiguredSujikoPuzzleExtractor(),
                cancellationToken)
            .BindAsExecutor(SujikoPuzzleWorkflowExecutorId);
        var expenseReportWorkflow = BuildExpenseReportWorkflow(
                expenseReportExtractor ?? new UnconfiguredExpenseReportExtractor(),
                expensePolicyOptions ?? new ExpensePolicyOptions(),
                cancellationToken)
            .BindAsExecutor(ExpenseReportWorkflowExecutorId);
        var unsupportedDocumentExecutor = new UnsupportedDocumentResultExecutor();

        return new WorkflowBuilder(classificationExecutor)
            .AddEdge<ClassifiedDocument>(
                classificationExecutor,
                receiptWorkflow,
                document => document is
                    { Classification.Category: DocumentCategory.Receipt },
                "receipt")
            .AddEdge<ClassifiedDocument>(
                classificationExecutor,
                shoppingListWorkflow,
                document => document is
                    { Classification.Category: DocumentCategory.ShoppingList },
                "shopping-list")
            .AddEdge<ClassifiedDocument>(
                classificationExecutor,
                sujikoPuzzleWorkflow,
                document => document is
                    { Classification.Category: DocumentCategory.SujikoPuzzle },
                "sujiko")
            .AddEdge<ClassifiedDocument>(
                classificationExecutor,
                expenseReportWorkflow,
                document => document is
                    { Classification.Category: DocumentCategory.ExpenseReport },
                "expense-report")
            .AddEdge<ClassifiedDocument>(
                classificationExecutor,
                unsupportedDocumentExecutor,
                document => document is
                    { Classification.Category: DocumentCategory.Invoice or DocumentCategory.Unknown },
                "unsupported")
            .WithOutputFrom(
                receiptWorkflow,
                shoppingListWorkflow,
                sujikoPuzzleWorkflow,
                expenseReportWorkflow,
                unsupportedDocumentExecutor)
            .WithName(DocumentRoutingWorkflowName)
            .WithDescription(
                "Classifies an image and routes it to exactly one document-processing workflow.")
            .Build();
    }

    public static string GetDestinationExecutorId(DocumentCategory category)
    {
        return category switch
        {
            DocumentCategory.Receipt => ReceiptWorkflowExecutorId,
            DocumentCategory.ShoppingList => ShoppingListWorkflowExecutorId,
            DocumentCategory.SujikoPuzzle => SujikoPuzzleWorkflowExecutorId,
            DocumentCategory.ExpenseReport => ExpenseReportWorkflowExecutorId,
            DocumentCategory.Invoice or DocumentCategory.Unknown => UnsupportedDocumentExecutorId,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown document category.")
        };
    }

    public static Microsoft.Agents.AI.Workflows.Workflow BuildReceiptWorkflow(
        IReceiptExtractor receiptExtractor,
        ReceiptPolicyOptions policyOptions,
        CancellationToken cancellationToken = default)
    {
        var extractionExecutor = new ReceiptExtractionExecutor(receiptExtractor, cancellationToken);
        var validationExecutor = new ReceiptValidationExecutor();
        var repairExecutor = new ReceiptValidationRepairExecutor(receiptExtractor, cancellationToken);
        var policyExecutor = new ReceiptPolicyExecutor(policyOptions);
        var resultExecutor = new ReceiptResultExecutor();

        return new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, repairExecutor)
            .AddEdge(repairExecutor, policyExecutor)
            .AddEdge(policyExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName(ReceiptWorkflowName)
            .WithDescription("Extracts, validates, repairs, and evaluates a receipt image.")
            .Build();
    }

    public static Microsoft.Agents.AI.Workflows.Workflow BuildShoppingListWorkflow(
        IShoppingListExtractor shoppingListExtractor,
        CancellationToken cancellationToken = default)
    {
        var extractionExecutor = new ShoppingListExtractionExecutor(
            shoppingListExtractor,
            cancellationToken);
        var validationExecutor = new ShoppingListValidationExecutor();
        var repairExecutor = new ShoppingListValidationRepairExecutor(
            shoppingListExtractor,
            cancellationToken);
        var resultExecutor = new ShoppingListResultExecutor();

        return new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, repairExecutor)
            .AddEdge(repairExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName(ShoppingListWorkflowName)
            .WithDescription("Extracts, validates, and repairs shopping list items from an image.")
            .Build();
    }

    public static Microsoft.Agents.AI.Workflows.Workflow BuildSujikoPuzzleWorkflow(
        ISujikoPuzzleExtractor sujikoPuzzleExtractor,
        CancellationToken cancellationToken = default)
    {
        var extractionExecutor = new SujikoPuzzleExtractionExecutor(
            sujikoPuzzleExtractor,
            cancellationToken);
        var validationExecutor = new SujikoPuzzleValidationExecutor();
        var repairExecutor = new SujikoPuzzleValidationRepairExecutor(
            sujikoPuzzleExtractor,
            cancellationToken);
        var resultExecutor = new SujikoPuzzleResultExecutor();

        return new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, repairExecutor)
            .AddEdge(repairExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName(SujikoPuzzleWorkflowName)
            .WithDescription("Extracts, validates, and repairs a Sujiko puzzle starting state from an image.")
            .Build();
    }

    public static Microsoft.Agents.AI.Workflows.Workflow BuildExpenseReportWorkflow(
        IExpenseReportExtractor expenseReportExtractor,
        ExpensePolicyOptions expensePolicyOptions,
        CancellationToken cancellationToken = default)
    {
        var extractionExecutor = new ExpenseReportExtractionExecutor(
            expenseReportExtractor,
            cancellationToken);
        var validationExecutor = new ExpenseReportValidationExecutor();
        var repairExecutor = new ExpenseReportValidationRepairExecutor(
            expenseReportExtractor,
            cancellationToken);
        var policyExecutor = new ExpenseReportPolicyExecutor(expensePolicyOptions);
        var resultExecutor = new ExpenseReportResultExecutor();

        return new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, repairExecutor)
            .AddEdge(repairExecutor, policyExecutor)
            .AddEdge(policyExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName(ExpenseReportWorkflowName)
            .WithDescription(
                "Extracts, validates, repairs, and evaluates an expense report image.")
            .Build();
    }

    private sealed class UnconfiguredSujikoPuzzleExtractor : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Sujiko puzzle extraction is not configured.");
        }
    }

    private sealed class UnconfiguredExpenseReportExtractor : IExpenseReportExtractor
    {
        public ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Expense report extraction is not configured.");
        }
    }
}

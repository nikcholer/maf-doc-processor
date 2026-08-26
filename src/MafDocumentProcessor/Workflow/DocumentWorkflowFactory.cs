using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public static class DocumentWorkflowFactory
{
    public const string ReceiptWorkflowName = "Receipt Processing";
    public const string ShoppingListWorkflowName = "Shopping List Processing";
    public const string SujikoPuzzleWorkflowName = "Sujiko Puzzle Processing";

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
}

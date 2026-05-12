using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ShoppingListValidationRepairExecutor(IShoppingListExtractor extractor)
    : Executor<ValidatedShoppingListExtraction, ValidatedShoppingListExtraction>("ShoppingListValidationRepair")
{
    public override async ValueTask<ValidatedShoppingListExtraction> HandleAsync(
        ValidatedShoppingListExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Validation.IsValid)
        {
            return message;
        }

        var extraction = message.Extraction;
        var repaired = await extractor.ExtractShoppingListAsync(
            extraction.ClassifiedDocument.Request,
            cancellationToken,
            message.Validation.Reasons);

        var repairedExtraction = extraction with
        {
            ShoppingList = repaired.Value,
            ExtractionUsages = [.. extraction.ExtractionUsages, repaired.Usage]
        };

        return new ValidatedShoppingListExtraction(
            repairedExtraction,
            ShoppingListValidationExecutor.Validate(repaired.Value));
    }
}

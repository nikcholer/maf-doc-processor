using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ShoppingListValidationExecutor()
    : Executor<ShoppingListExtraction, ValidatedShoppingListExtraction>("ShoppingListValidation")
{
    public override ValueTask<ValidatedShoppingListExtraction> HandleAsync(
        ShoppingListExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var reasons = new List<string>();
        var items = message.ShoppingList.Items;

        if (items.Count == 0)
        {
            reasons.Add("Shopping list contains no readable items.");
        }

        if (items.Any(item => string.IsNullOrWhiteSpace(item.Name)))
        {
            reasons.Add("Shopping list contains an item without a readable name.");
        }

        var validation = reasons.Count == 0
            ? ValidationResult.Valid
            : new ValidationResult(false, reasons);

        return ValueTask.FromResult(new ValidatedShoppingListExtraction(message, validation));
    }
}

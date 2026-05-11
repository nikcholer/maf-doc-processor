using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ShoppingListExtractionExecutor(IShoppingListExtractor extractor)
    : Executor<ClassifiedDocument, ShoppingListExtraction>("ShoppingListExtraction")
{
    public override async ValueTask<ShoppingListExtraction> HandleAsync(
        ClassifiedDocument message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Classification.Category != DocumentCategory.ShoppingList)
        {
            throw new InvalidOperationException(
                $"Shopping list extraction received a {message.Classification.Category} document.");
        }

        var extraction = await extractor.ExtractShoppingListAsync(message.Request, cancellationToken);
        return new ShoppingListExtraction(
            message,
            extraction.Value,
            extraction.Usage);
    }
}

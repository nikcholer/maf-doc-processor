using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ShoppingListExtraction(
    ClassifiedDocument ClassifiedDocument,
    ShoppingListData ShoppingList,
    IReadOnlyList<ModelTokenUsage> ExtractionUsages)
{
    public ShoppingListExtraction(
        ClassifiedDocument classifiedDocument,
        ShoppingListData shoppingList,
        ModelTokenUsage extractionUsage)
        : this(classifiedDocument, shoppingList, [extractionUsage])
    {
    }
}

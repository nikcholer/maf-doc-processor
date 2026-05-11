using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ShoppingListExtraction(
    ClassifiedDocument ClassifiedDocument,
    ShoppingListData ShoppingList,
    ModelTokenUsage ExtractionUsage);

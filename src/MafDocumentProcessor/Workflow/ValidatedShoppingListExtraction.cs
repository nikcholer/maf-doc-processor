using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ValidatedShoppingListExtraction(
    ShoppingListExtraction Extraction,
    ValidationResult Validation);

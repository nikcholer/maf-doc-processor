namespace MafDocumentProcessor.Domain;

public sealed record DocumentProcessingResult(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    DocumentModelUsage ModelUsage,
    ReceiptData? Receipt,
    ShoppingListData? ShoppingList,
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

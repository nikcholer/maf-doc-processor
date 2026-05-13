namespace MafDocumentProcessor.Domain;

public sealed record DocumentProcessingResult(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    DocumentModelUsage ModelUsage,
    ReceiptData? Receipt,
    ShoppingListData? ShoppingList,
    SujikoPuzzleData? SujikoPuzzle,
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation,
    HumanReviewResult HumanReview,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

namespace MafDocumentProcessor.Domain;

public sealed record ReceiptProcessingResult(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    DocumentModelUsage ModelUsage,
    ReceiptData? Receipt,
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

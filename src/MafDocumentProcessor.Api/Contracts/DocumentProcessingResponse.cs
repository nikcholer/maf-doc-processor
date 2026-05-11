using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Contracts;

public sealed record DocumentProcessingResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    DocumentModelUsage ModelUsage,
    ReceiptDocumentResponse? Document,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ReceiptDocumentResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    ReceiptData Data,
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation);

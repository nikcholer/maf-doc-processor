using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Contracts;

public sealed record DocumentProcessingResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    DocumentModelUsage ModelUsage,
    ProcessedDocumentResponse? Document,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ProcessedDocumentResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    object Data,
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation);

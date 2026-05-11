namespace MafDocumentProcessor.Domain;

public sealed record DocumentClassification(
    DocumentCategory Category,
    decimal? Confidence,
    string ConfidenceReasoning,
    string? DocumentTypeDescription = null);

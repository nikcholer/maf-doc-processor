using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ReceiptExtraction(
    ClassifiedDocument ClassifiedDocument,
    ReceiptData Receipt,
    ModelTokenUsage ExtractionUsage);

using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ReceiptExtraction(
    ClassifiedDocument ClassifiedDocument,
    ReceiptData Receipt,
    IReadOnlyList<ModelTokenUsage> ExtractionUsages)
{
    public ReceiptExtraction(
        ClassifiedDocument classifiedDocument,
        ReceiptData receipt,
        ModelTokenUsage extractionUsage)
        : this(classifiedDocument, receipt, [extractionUsage])
    {
    }
}

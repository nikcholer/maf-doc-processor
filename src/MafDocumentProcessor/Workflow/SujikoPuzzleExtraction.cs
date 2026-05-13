using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record SujikoPuzzleExtraction(
    ClassifiedDocument ClassifiedDocument,
    SujikoPuzzleData SujikoPuzzle,
    IReadOnlyList<ModelTokenUsage> ExtractionUsages)
{
    public SujikoPuzzleExtraction(
        ClassifiedDocument classifiedDocument,
        SujikoPuzzleData sujikoPuzzle,
        ModelTokenUsage extractionUsage)
        : this(classifiedDocument, sujikoPuzzle, [extractionUsage])
    {
    }
}

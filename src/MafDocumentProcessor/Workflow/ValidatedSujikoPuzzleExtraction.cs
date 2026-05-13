using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ValidatedSujikoPuzzleExtraction(
    SujikoPuzzleExtraction Extraction,
    ValidationResult Validation);

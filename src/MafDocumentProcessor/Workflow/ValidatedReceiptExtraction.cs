using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ValidatedReceiptExtraction(
    ReceiptExtraction Extraction,
    ValidationResult Validation);

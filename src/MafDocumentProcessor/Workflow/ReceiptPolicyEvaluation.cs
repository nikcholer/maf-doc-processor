using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ReceiptPolicyEvaluation(
    ValidatedReceiptExtraction ValidatedExtraction,
    ReceiptPolicyResult PolicyResult,
    ValidationResult Validation);

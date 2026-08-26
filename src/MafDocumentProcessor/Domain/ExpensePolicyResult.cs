namespace MafDocumentProcessor.Domain;

public sealed record ExpensePolicyResult(
    bool IsWithinHighValueThreshold,
    bool AllLinesHaveReceiptReferences,
    PolicyDecision Decision,
    IReadOnlyList<string> Reasons);

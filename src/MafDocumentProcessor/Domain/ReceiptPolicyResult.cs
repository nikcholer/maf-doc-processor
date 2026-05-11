namespace MafDocumentProcessor.Domain;

public sealed record ReceiptPolicyResult(
    bool IsWithinReviewThreshold,
    bool HasPaymentMethod,
    PolicyDecision Decision,
    IReadOnlyList<string> Reasons);

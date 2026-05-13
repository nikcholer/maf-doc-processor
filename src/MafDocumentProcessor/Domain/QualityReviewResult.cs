namespace MafDocumentProcessor.Domain;

public enum QualityReviewDecision
{
    Accept,
    NeedsHumanReview,
    Reject
}

public enum QualityReviewFindingSeverity
{
    Info,
    Warning,
    Error
}

public sealed record QualityReviewFinding(
    QualityReviewFindingSeverity Severity,
    string Message);

public sealed record QualityReviewResult(
    QualityReviewDecision Decision,
    IReadOnlyList<QualityReviewFinding> Findings,
    DocumentModelUsage ModelUsage);

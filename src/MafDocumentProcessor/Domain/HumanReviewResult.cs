namespace MafDocumentProcessor.Domain;

public enum HumanReviewStatus
{
    NotRequired,
    Recommended,
    Required
}

public enum ReviewerDecision
{
    Approved,
    Rejected,
    CorrectionRequested
}

public sealed record HumanReviewResult(
    HumanReviewStatus Status,
    IReadOnlyList<string> Reasons,
    bool RequiresUserAttestation,
    string? AttestationPrompt)
{
    public bool IsRequired => Status == HumanReviewStatus.Required;

    public bool IsRecommended => Status != HumanReviewStatus.NotRequired;

    public static HumanReviewResult NotRequired { get; } =
        new(HumanReviewStatus.NotRequired, [], RequiresUserAttestation: false, AttestationPrompt: null);
}

public sealed record ReviewerInput(
    string ReviewerId,
    ReviewerDecision Decision,
    IReadOnlyList<string> Notes,
    DateTimeOffset ReviewedAt);

public sealed record ReviewDecisionLogEntry(
    string DocumentId,
    ReviewerInput ReviewerInput,
    HumanReviewResult ReviewState);

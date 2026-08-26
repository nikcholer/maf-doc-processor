namespace MafDocumentProcessor.Workflow;

public sealed record CaptureRegionValidationCompletedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    bool IsSuccess,
    int ProposedRegionCount,
    int AcceptedRegionCount,
    int RejectedRegionCount,
    IReadOnlyList<string> ErrorCodes);

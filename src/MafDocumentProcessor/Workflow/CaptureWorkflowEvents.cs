namespace MafDocumentProcessor.Workflow;

public sealed record CaptureStartedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    int SourceCount);

public sealed record CaptureSourceCompletedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    bool IsSuccess,
    int ProposedRegionCount,
    int AcceptedRegionCount,
    IReadOnlyList<string> ErrorCodes);

public sealed record CaptureSourcesAggregatedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    int SourceCount,
    int MembersToProcess,
    int RejectedRegionCount);

public sealed record CaptureMemberStartedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    string MemberId);

public sealed record CaptureMemberCompletedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    string MemberId,
    bool IsSuccess,
    string Disposition);

public sealed record CaptureCompletedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string Status,
    int SourceCount,
    int MemberCount);

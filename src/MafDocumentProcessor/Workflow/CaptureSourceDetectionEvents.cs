namespace MafDocumentProcessor.Workflow;

public sealed record CaptureSourceDecodedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    int OriginalWidthPixels,
    int OriginalHeightPixels,
    int OrientedWidthPixels,
    int OrientedHeightPixels);

public sealed record CaptureSourceDetectionCompletedEvent(
    string TraceId,
    string CaptureId,
    string? SourceId,
    string SourceItemId,
    bool IsSuccess,
    int ProposalCount,
    string? ModelId,
    IReadOnlyList<string> ErrorCodes,
    bool UsedRegionOverrides = false);

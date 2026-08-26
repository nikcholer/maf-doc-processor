using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record CaptureWorkflowContext
{
    public CaptureWorkflowContext(
        string traceId,
        string captureId,
        string? sourceId = null,
        string? sourceItemId = null,
        string? memberId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);

        TraceId = traceId;
        CaptureId = captureId;
        SourceId = sourceId;
        SourceItemId = sourceItemId;
        MemberId = memberId;
    }

    public string TraceId { get; }

    public string CaptureId { get; }

    public string? SourceId { get; }

    public string? SourceItemId { get; }

    public string? MemberId { get; }

    public CaptureWorkflowContext ForSource(string sourceItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        return new CaptureWorkflowContext(TraceId, CaptureId, SourceId, sourceItemId);
    }

    public CaptureWorkflowContext ForMember(string sourceItemId, string memberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        return new CaptureWorkflowContext(TraceId, CaptureId, SourceId, sourceItemId, memberId);
    }
}

public sealed record CaptureSourceDetectionInput(
    CaptureWorkflowContext Context,
    CompositeCaptureSource Source);

public sealed record CaptureSourceDetectionOutput(
    CaptureWorkflowContext Context,
    CompositeCaptureSource Source,
    CaptureSourceImageMetadata? ImageMetadata,
    OrientedCaptureSourceImage? OrientedSource,
    IReadOnlyList<DocumentRegionProposal> Proposals,
    DocumentModelUsage ModelUsage,
    IReadOnlyList<CaptureProcessingError> Errors,
    IReadOnlyList<string> Warnings) : IDisposable
{
    public bool IsSuccess => Errors.Count == 0;

    public void Dispose()
    {
        OrientedSource?.Dispose();
    }
}

public sealed record CaptureRegionValidationInput(
    CaptureWorkflowContext Context,
    CompositeCaptureSource Source,
    CaptureSourceDetectionOutput Detection);

public sealed record CaptureMemberProcessingInput(
    CaptureWorkflowContext Context,
    CaptureMember Member,
    FileRequest CropRequest,
    PixelRectangle CropPixels);

public sealed record CaptureRejectedRegion(
    string SourceItemId,
    int DetectionIndex,
    ProposedNormalizedBounds Bounds,
    NormalizedBounds? TrustedBounds,
    CaptureProcessingError Error);

public sealed record CaptureRegionValidationOutput(
    CaptureWorkflowContext Context,
    CompositeCaptureSource Source,
    CaptureSourceImageMetadata? ImageMetadata,
    IReadOnlyList<CaptureMemberProcessingInput> AcceptedMembers,
    IReadOnlyList<CaptureRejectedRegion> RejectedRegions,
    DocumentModelUsage ModelUsage,
    IReadOnlyList<CaptureProcessingError> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Errors.Count == 0;
}

public sealed record CaptureAggregationInput(
    CaptureWorkflowContext Context,
    IReadOnlyList<CaptureSourceResult> Sources,
    IReadOnlyList<CaptureMemberResult> Members,
    IReadOnlyList<CaptureProcessingError> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CaptureSourceWork(
    CaptureWorkflowContext Context,
    CompositeCaptureRequest Request);

public sealed record CaptureProcessedSource(
    CompositeCaptureSource Source,
    CaptureSourceImageMetadata? ImageMetadata,
    int ProposedRegionCount,
    DocumentModelUsage DetectionUsage,
    IReadOnlyList<CaptureMemberProcessingInput> AcceptedMembers,
    IReadOnlyList<CaptureRejectedRegion> RejectedRegions,
    IReadOnlyList<CaptureProcessingError> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CaptureSourceLaneResult(
    CaptureWorkflowContext Context,
    CompositeCaptureRequest Request,
    int LaneIndex,
    IReadOnlyList<CaptureProcessedSource> Sources);

public sealed record CaptureSourceStageResult(
    CaptureWorkflowContext Context,
    CompositeCaptureRequest Request,
    IReadOnlyList<CaptureProcessedSource> Sources,
    IReadOnlyList<CaptureMemberProcessingInput> MembersToProcess,
    IReadOnlyList<CaptureRejectedRegion> AdditionalRejectedRegions);

public sealed record CaptureMemberWork(
    CaptureWorkflowContext Context,
    CaptureSourceStageResult Stage,
    IReadOnlyList<CaptureMemberProcessingInput> Members);

public sealed record CaptureMemberLaneResult(
    CaptureWorkflowContext Context,
    CaptureSourceStageResult Stage,
    int LaneIndex,
    IReadOnlyList<CaptureMemberWorkflowOutcome> Outcomes);

public sealed record CaptureMemberWorkflowOutcome(
    CaptureMemberProcessingInput Member,
    DocumentProcessingResult? Result,
    CaptureProcessingError? Error);

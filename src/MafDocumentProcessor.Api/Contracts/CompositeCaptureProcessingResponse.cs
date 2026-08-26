using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Contracts;

public sealed record CompositeCaptureProcessingResponse(
    string CaptureId,
    CompositeCaptureMetadata Metadata,
    IReadOnlyList<CompositeCaptureSourceResponse> Sources,
    DocumentModelUsage ModelUsage,
    CaptureProcessingStatus Status,
    IReadOnlyList<CompositeCaptureMemberResponse> Members,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CompositeCaptureSourceResponse(
    string SourceItemId,
    int Index,
    CaptureSourceMetadata Metadata,
    CaptureDetectionSummary Detection,
    CaptureProcessingStatus Status,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record CompositeCaptureMemberResponse(
    string SourceItemId,
    string MemberId,
    int Index,
    CaptureRegionResponse Region,
    CaptureMemberStatus Status,
    CaptureMemberDisposition Disposition,
    IReadOnlyList<string> DispositionReasons,
    DocumentProcessingResponse? Result,
    ApiErrorResponse? Error);

public sealed record CaptureRegionResponse(
    string SourceItemId,
    string MemberId,
    int Index,
    NormalizedBounds Bounds,
    IReadOnlyList<NormalizedPoint>? Outline,
    decimal? Confidence,
    IReadOnlyList<string> Warnings);

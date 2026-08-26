using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Contracts;

public sealed record CompositeCaptureRegionOverridesRequest(
    IReadOnlyList<CaptureSourceRegionOverridesRequest> Sources);

public sealed record CaptureSourceRegionOverridesRequest(
    int SourceIndex,
    IReadOnlyList<CaptureRegionOverrideRequest> Regions);

public sealed record CaptureRegionOverrideRequest(
    ProposedNormalizedBounds Bounds,
    IReadOnlyList<ProposedNormalizedPoint>? Outline = null);

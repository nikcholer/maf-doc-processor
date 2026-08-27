using System.Text.Json.Serialization;

namespace MafDocumentProcessor.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureProcessingStatus>))]
public enum CaptureProcessingStatus
{
    Succeeded,
    PartiallySucceeded,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<CaptureMemberStatus>))]
public enum CaptureMemberStatus
{
    Processed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter<CaptureMemberDisposition>))]
public enum CaptureMemberDisposition
{
    Accepted,
    Review,
    Rejected
}

public static class CaptureIdentifiers
{
    public static string SourceItemId(int oneBasedSourceIndex)
    {
        RequirePositive(oneBasedSourceIndex, nameof(oneBasedSourceIndex));
        return $"source-{oneBasedSourceIndex:D3}";
    }

    public static string MemberId(string sourceItemId, int oneBasedMemberIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        RequirePositive(oneBasedMemberIndex, nameof(oneBasedMemberIndex));
        return $"{sourceItemId}-document-{oneBasedMemberIndex:D3}";
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The index must be one or greater.");
        }
    }
}

public sealed record CompositeCaptureRequest
{
    private CompositeCaptureRequest(
        string captureId,
        DateTimeOffset receivedAt,
        string? sourceId,
        string? traceId,
        IReadOnlyList<CompositeCaptureSource> sources)
    {
        CaptureId = captureId;
        ReceivedAt = receivedAt;
        SourceId = sourceId;
        TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId;
        Sources = Array.AsReadOnly(sources.ToArray());
    }

    public string CaptureId { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string? SourceId { get; }

    public string? TraceId { get; }

    public IReadOnlyList<CompositeCaptureSource> Sources { get; }

    public static CompositeCaptureRequest Create(
        IReadOnlyList<FileRequest> sourceRequests,
        DateTimeOffset receivedAt,
        string? sourceId = null,
        string? captureId = null,
        IReadOnlyDictionary<int, IReadOnlyList<CaptureRegionOverride>>? regionOverridesBySourceIndex = null,
        string? traceId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceRequests);
        if (sourceRequests.Count == 0)
        {
            throw new ArgumentException("A composite capture requires at least one source.", nameof(sourceRequests));
        }

        var assignedCaptureId = string.IsNullOrWhiteSpace(captureId)
            ? $"capture-{Guid.NewGuid():N}"
            : captureId;
        ValidateOverrideSourceIndexes(sourceRequests.Count, regionOverridesBySourceIndex);
        var sources = sourceRequests
            .Select((request, index) => CreateSource(
                request,
                index + 1,
                sourceId,
                regionOverridesBySourceIndex))
            .ToArray();

        return new CompositeCaptureRequest(assignedCaptureId, receivedAt, sourceId, traceId, sources);
    }

    private static CompositeCaptureSource CreateSource(
        FileRequest request,
        int sourceIndex,
        string? sourceId,
        IReadOnlyDictionary<int, IReadOnlyList<CaptureRegionOverride>>? regionOverridesBySourceIndex)
    {
        var sourceItemId = CaptureIdentifiers.SourceItemId(sourceIndex);
        IReadOnlyList<DocumentRegionProposal>? proposals = null;
        if (regionOverridesBySourceIndex?.TryGetValue(sourceIndex, out var overrides) == true)
        {
            ArgumentNullException.ThrowIfNull(overrides);
            proposals = Array.AsReadOnly(overrides
                .Select((region, index) =>
                {
                    ArgumentNullException.ThrowIfNull(region);
                    return new DocumentRegionProposal(
                        sourceItemId,
                        index + 1,
                        region.Bounds,
                        region.Outline,
                        confidence: null);
                })
                .ToArray());
        }

        return new CompositeCaptureSource(
            sourceItemId,
            sourceIndex,
            request with { SourceId = sourceId },
            proposals);
    }

    private static void ValidateOverrideSourceIndexes(
        int sourceCount,
        IReadOnlyDictionary<int, IReadOnlyList<CaptureRegionOverride>>? regionOverridesBySourceIndex)
    {
        if (regionOverridesBySourceIndex is null)
        {
            return;
        }

        if (regionOverridesBySourceIndex.Keys.Any(index => index <= 0 || index > sourceCount))
        {
            var invalidIndex = regionOverridesBySourceIndex.Keys.First(index => index <= 0 || index > sourceCount);
            throw new ArgumentOutOfRangeException(
                nameof(regionOverridesBySourceIndex),
                invalidIndex,
                $"Region override source indexes must be between one and {sourceCount}.");
        }
    }
}

public sealed record CompositeCaptureSource
{
    public CompositeCaptureSource(
        string sourceItemId,
        int index,
        FileRequest request,
        IReadOnlyList<DocumentRegionProposal>? regionOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(request);
        if (index <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The source index must be one or greater.");
        }

        SourceItemId = sourceItemId;
        Index = index;
        Request = request;
        if (regionOverrides is not null)
        {
            var invalidProposal = regionOverrides.FirstOrDefault(proposal =>
                !string.Equals(proposal.SourceItemId, sourceItemId, StringComparison.Ordinal));
            if (invalidProposal is not null)
            {
                throw new ArgumentException(
                    "Every region override must belong to its capture source.",
                    nameof(regionOverrides));
            }
        }

        RegionOverrides = regionOverrides is null
            ? null
            : Array.AsReadOnly(regionOverrides.ToArray());
    }

    public string SourceItemId { get; }

    public int Index { get; }

    public FileRequest Request { get; }

    public IReadOnlyList<DocumentRegionProposal>? RegionOverrides { get; }
}

public sealed record DetectedDocumentRegion
{
    public DetectedDocumentRegion(
        string sourceItemId,
        int detectionIndex,
        NormalizedBounds bounds,
        IReadOnlyList<NormalizedPoint>? outline = null,
        decimal? confidence = null,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(bounds);
        if (detectionIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionIndex),
                detectionIndex,
                "The detection index must be one or greater.");
        }

        if (outline is { Count: not 4 })
        {
            throw new ArgumentException("A document outline must contain exactly four points.", nameof(outline));
        }

        if (confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Detection confidence must be between zero and one.");
        }

        SourceItemId = sourceItemId;
        DetectionIndex = detectionIndex;
        Bounds = bounds;
        Outline = outline is null ? null : Array.AsReadOnly(outline.ToArray());
        Confidence = confidence;
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
    }

    public string SourceItemId { get; }

    public int DetectionIndex { get; }

    public NormalizedBounds Bounds { get; }

    public IReadOnlyList<NormalizedPoint>? Outline { get; }

    public decimal? Confidence { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed record CaptureMember
{
    public CaptureMember(
        string sourceItemId,
        string memberId,
        int index,
        int sourceMemberIndex,
        DetectedDocumentRegion region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        ArgumentNullException.ThrowIfNull(region);
        if (!string.Equals(sourceItemId, region.SourceItemId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The member and region must belong to the same source.", nameof(region));
        }

        if (index <= 0 || sourceMemberIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Member indexes must be one or greater.");
        }

        var expectedMemberId = CaptureIdentifiers.MemberId(sourceItemId, sourceMemberIndex);
        if (!string.Equals(memberId, expectedMemberId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The member identifier must be {expectedMemberId} for its source position.",
                nameof(memberId));
        }

        SourceItemId = sourceItemId;
        MemberId = memberId;
        Index = index;
        SourceMemberIndex = sourceMemberIndex;
        Region = region;
    }

    public string SourceItemId { get; }

    public string MemberId { get; }

    public int Index { get; }

    public int SourceMemberIndex { get; }

    public DetectedDocumentRegion Region { get; }
}

public sealed record CaptureProcessingError(string Code, string Message, string? Target = null);

public sealed record CaptureSourceMetadata(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset ReceivedAt,
    int? OrientedWidthPixels,
    int? OrientedHeightPixels);

public sealed record CaptureDetectionSummary
{
    public CaptureDetectionSummary(
        string? modelId,
        int proposedRegionCount,
        int acceptedRegionCount,
        IReadOnlyList<string> warnings,
        bool usedRegionOverrides = false)
    {
        if (proposedRegionCount < 0 || acceptedRegionCount < 0 || acceptedRegionCount > proposedRegionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acceptedRegionCount),
                "Region counts cannot be negative, and accepted regions cannot exceed proposed regions.");
        }

        ModelId = modelId;
        ProposedRegionCount = proposedRegionCount;
        AcceptedRegionCount = acceptedRegionCount;
        Warnings = Array.AsReadOnly(warnings.ToArray());
        UsedRegionOverrides = usedRegionOverrides;
    }

    public string? ModelId { get; }

    public int ProposedRegionCount { get; }

    public int AcceptedRegionCount { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool UsedRegionOverrides { get; }
}

public sealed record CaptureSourceResult
{
    public CaptureSourceResult(
        string sourceItemId,
        int index,
        CaptureSourceMetadata metadata,
        CaptureDetectionSummary detection,
        CaptureProcessingStatus status,
        DocumentModelUsage modelUsage,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        if (index <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The source index must be one or greater.");
        }

        SourceItemId = sourceItemId;
        Index = index;
        Metadata = metadata;
        Detection = detection;
        Status = status;
        ModelUsage = modelUsage;
        Errors = Array.AsReadOnly(errors.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }

    public string SourceItemId { get; }

    public int Index { get; }

    public CaptureSourceMetadata Metadata { get; }

    public CaptureDetectionSummary Detection { get; }

    public CaptureProcessingStatus Status { get; }

    public DocumentModelUsage ModelUsage { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }
}

public sealed record CaptureMemberResult
{
    public CaptureMemberResult(
        CaptureMember member,
        CaptureMemberStatus status,
        CaptureMemberDisposition disposition,
        IReadOnlyList<string> dispositionReasons,
        DocumentProcessingResult? result,
        CaptureProcessingError? error)
    {
        if (status == CaptureMemberStatus.Processed && (result is null || error is not null))
        {
            throw new ArgumentException("A processed member must contain a result and no error.", nameof(result));
        }

        if (status == CaptureMemberStatus.Failed && (result is not null || error is null))
        {
            throw new ArgumentException("A failed member must contain an error and no result.", nameof(error));
        }

        if (status == CaptureMemberStatus.Failed && disposition != CaptureMemberDisposition.Rejected)
        {
            throw new ArgumentException("A failed member must have the Rejected disposition.", nameof(disposition));
        }

        Member = member;
        Status = status;
        Disposition = disposition;
        DispositionReasons = Array.AsReadOnly(dispositionReasons.ToArray());
        Result = result;
        Error = error;
    }

    public CaptureMember Member { get; }

    public CaptureMemberStatus Status { get; }

    public CaptureMemberDisposition Disposition { get; }

    public IReadOnlyList<string> DispositionReasons { get; }

    public DocumentProcessingResult? Result { get; }

    public CaptureProcessingError? Error { get; }
}

public sealed record CompositeCaptureMetadata(
    DateTimeOffset ReceivedAt,
    string? SourceId,
    int SourceCount,
    long AggregateFileSizeBytes);

public sealed record CompositeCaptureResult
{
    public CompositeCaptureResult(
        string captureId,
        CompositeCaptureMetadata metadata,
        IReadOnlyList<CaptureSourceResult> sources,
        DocumentModelUsage modelUsage,
        CaptureProcessingStatus status,
        IReadOnlyList<CaptureMemberResult> members,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);

        CaptureId = captureId;
        Metadata = metadata;
        Sources = Array.AsReadOnly(sources.ToArray());
        ModelUsage = modelUsage;
        Status = status;
        Members = Array.AsReadOnly(members.ToArray());
        Errors = Array.AsReadOnly(errors.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }

    public string CaptureId { get; }

    public CompositeCaptureMetadata Metadata { get; }

    public IReadOnlyList<CaptureSourceResult> Sources { get; }

    public DocumentModelUsage ModelUsage { get; }

    public CaptureProcessingStatus Status { get; }

    public IReadOnlyList<CaptureMemberResult> Members { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }
}

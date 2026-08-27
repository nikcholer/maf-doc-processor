using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;

namespace MafDocumentProcessor.Services;

public sealed class CaptureRegionValidationService(
    CompositeCaptureOptions options,
    ILogger<CaptureRegionValidationService>? logger = null) : ICaptureRegionValidationService
{
    public const string OverlapWarning = "detected regions overlap";
    public const string InvalidDetectedRegionCode = "invalid_detected_region";
    public const string NoUsableDocumentRegionCode = "no_usable_document_region";

    private readonly ILogger<CaptureRegionValidationService> _logger =
        logger ?? NullLogger<CaptureRegionValidationService>.Instance;

    public async ValueTask<CaptureRegionValidationOutput> ValidateAsync(
        CaptureRegionValidationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var detection = input.Detection;
        var context = input.Context.ForSource(input.Source.SourceItemId);
        if (!detection.IsSuccess)
        {
            return PassThroughFailure(context, input.Source, detection);
        }

        if (detection.OrientedSource is null)
        {
            return SourceFailure(
                context,
                input.Source,
                detection,
                NoUsableDocumentRegionCode,
                $"Source '{input.Source.SourceItemId}' did not contain a usable document region.");
        }

        var orientedSource = detection.OrientedSource;
        var candidates = new List<RegionCandidate>();
        var rejected = new List<CaptureRejectedRegion>();
        foreach (var proposal in detection.Proposals)
        {
            if (TryCreateCandidate(proposal, orientedSource, out var candidate, out var rejectedRegion))
            {
                candidates.Add(candidate);
            }
            else if (rejectedRegion is not null)
            {
                rejected.Add(rejectedRegion);
            }
        }

        var usesRegionOverrides = input.Source.RegionOverrides is not null;
        var acceptedCandidates = usesRegionOverrides
            ? ApplyOverrideLimitPolicy(candidates, rejected)
            : ApplyDuplicateAndLimitPolicy(candidates, rejected);
        if (!usesRegionOverrides)
        {
            ApplyEdgePadding(acceptedCandidates, orientedSource);
            ApplyOverlapWarnings(acceptedCandidates);
        }

        var acceptedMembers = new List<CaptureMemberProcessingInput>(acceptedCandidates.Count);
        for (var index = 0; index < acceptedCandidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            acceptedMembers.Add(await CreateAcceptedMemberAsync(
                context,
                input.Source,
                orientedSource,
                acceptedCandidates[index],
                index + 1,
                cancellationToken));
        }

        var warnings = acceptedMembers
            .SelectMany(member => member.Member.Region.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var errors = acceptedMembers.Count == 0
            ?
            [
                new CaptureProcessingError(
                    NoUsableDocumentRegionCode,
                    $"Source '{input.Source.SourceItemId}' did not contain a usable document region.",
                    input.Source.SourceItemId)
            ]
            : Array.Empty<CaptureProcessingError>();

        _logger.LogInformation(
            "Validated {ProposedCount} region proposals for {CaptureId}/{SourceItemId}. Accepted={AcceptedCount}, Rejected={RejectedCount}.",
            detection.Proposals.Count,
            context.CaptureId,
            input.Source.SourceItemId,
            acceptedMembers.Count,
            rejected.Count);

        return new CaptureRegionValidationOutput(
            context,
            input.Source,
            detection.ImageMetadata,
            acceptedMembers,
            rejected,
            detection.ModelUsage,
            errors,
            warnings);
    }

    private bool TryCreateCandidate(
        DocumentRegionProposal proposal,
        OrientedCaptureSourceImage source,
        out RegionCandidate candidate,
        out CaptureRejectedRegion? rejected)
    {
        candidate = null!;
        if (!string.Equals(proposal.SourceItemId, source.Source.SourceItemId, StringComparison.Ordinal))
        {
            rejected = Reject(
                proposal,
                trustedBounds: null,
                "does not belong to this source.");
            return false;
        }

        if (!CaptureRegionGeometry.TryCreateTrustedBounds(
                proposal.Bounds,
                options.MinRegionWidth,
                options.MinRegionHeight,
                options.MinRegionArea,
                out var bounds,
                out var rejection))
        {
            rejected = Reject(
                proposal,
                trustedBounds: null,
                rejection == CaptureRegionGeometryRejection.BelowMinimumSize
                    ? "is smaller than the configured useful-region threshold."
                    : "bounds are not finite, not positive, or not inside the normalized image.");
            return false;
        }

        var pixels = CaptureRegionGeometry.MapToPixels(
            bounds!,
            source.WidthPixels,
            source.HeightPixels);
        if (pixels.IsEmpty)
        {
            rejected = Reject(
                proposal,
                bounds,
                "maps to an empty pixel crop.");
            return false;
        }

        CaptureRegionGeometry.TryCreateTrustedOutline(proposal.Outline, out var outline);
        candidate = new RegionCandidate
        {
            Proposal = proposal,
            Bounds = bounds!,
            Pixels = pixels,
            Outline = outline,
            Confidence = CaptureRegionGeometry.TrustConfidence(proposal.Confidence)
        };
        rejected = null;
        return true;
    }

    private IReadOnlyList<RegionCandidate> ApplyDuplicateAndLimitPolicy(
        List<RegionCandidate> candidates,
        List<CaptureRejectedRegion> rejected)
    {
        var ordered = candidates
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Proposal.DetectionIndex)
            .ToArray();
        var kept = new List<RegionCandidate>();
        foreach (var candidate in ordered)
        {
            var duplicateOf = kept.FirstOrDefault(existing =>
                CaptureRegionGeometry.IntersectionOverUnion(existing.Bounds, candidate.Bounds)
                >= options.DuplicateIntersectionOverUnionThreshold);
            if (duplicateOf is not null)
            {
                rejected.Add(Reject(
                    candidate.Proposal,
                    candidate.Bounds,
                    $"is a duplicate of detected region {duplicateOf.Proposal.DetectionIndex}."));
                continue;
            }

            kept.Add(candidate);
        }

        var maxAccepted = Math.Min(options.MaxDetectedRegionsPerSource, options.MaxMembersPerCapture);
        foreach (var extra in kept.Skip(maxAccepted))
        {
            rejected.Add(Reject(
                extra.Proposal,
                extra.Bounds,
                "exceeds the configured member limit."));
        }

        return kept.Take(maxAccepted).ToArray();
    }

    private IReadOnlyList<RegionCandidate> ApplyOverrideLimitPolicy(
        List<RegionCandidate> candidates,
        List<CaptureRejectedRegion> rejected)
    {
        var maxAccepted = Math.Min(options.MaxDetectedRegionsPerSource, options.MaxMembersPerCapture);
        foreach (var extra in candidates.Skip(maxAccepted))
        {
            rejected.Add(Reject(
                extra.Proposal,
                extra.Bounds,
                "exceeds the configured member limit."));
        }

        return candidates.Take(maxAccepted).ToArray();
    }

    private void ApplyEdgePadding(
        IReadOnlyList<RegionCandidate> accepted,
        OrientedCaptureSourceImage source)
    {
        if (options.RegionEdgePadding <= 0)
        {
            return;
        }

        foreach (var candidate in accepted)
        {
            candidate.Bounds = CaptureRegionGeometry.Expand(candidate.Bounds, options.RegionEdgePadding);
            candidate.Pixels = CaptureRegionGeometry.MapToPixels(
                candidate.Bounds,
                source.WidthPixels,
                source.HeightPixels);
        }
    }

    private void ApplyOverlapWarnings(IReadOnlyList<RegionCandidate> accepted)
    {
        for (var left = 0; left < accepted.Count; left++)
        {
            for (var right = left + 1; right < accepted.Count; right++)
            {
                if (CaptureRegionGeometry.IntersectionOverUnion(
                        accepted[left].Bounds,
                        accepted[right].Bounds)
                    >= options.OverlapReviewIntersectionOverUnionThreshold)
                {
                    accepted[left].Warnings.Add(OverlapWarning);
                    accepted[right].Warnings.Add(OverlapWarning);
                }
            }
        }
    }

    private static async ValueTask<CaptureMemberProcessingInput> CreateAcceptedMemberAsync(
        CaptureWorkflowContext context,
        CompositeCaptureSource source,
        OrientedCaptureSourceImage orientedSource,
        RegionCandidate candidate,
        int sourceMemberIndex,
        CancellationToken cancellationToken)
    {
        var memberId = CaptureIdentifiers.MemberId(source.SourceItemId, sourceMemberIndex);
        var region = new DetectedDocumentRegion(
            source.SourceItemId,
            candidate.Proposal.DetectionIndex,
            candidate.Bounds,
            candidate.Outline,
            candidate.Confidence,
            candidate.Warnings.Distinct(StringComparer.Ordinal).ToArray());
        var member = new CaptureMember(
            source.SourceItemId,
            memberId,
            sourceMemberIndex,
            sourceMemberIndex,
            region);
        var cropRequest = await CreateCropRequestAsync(
            source,
            orientedSource,
            candidate.Pixels,
            memberId,
            cancellationToken);

        return new CaptureMemberProcessingInput(
            context.ForMember(source.SourceItemId, memberId),
            member,
            cropRequest,
            candidate.Pixels);
    }

    private static async ValueTask<FileRequest> CreateCropRequestAsync(
        CompositeCaptureSource source,
        OrientedCaptureSourceImage orientedSource,
        PixelRectangle pixels,
        string memberId,
        CancellationToken cancellationToken)
    {
        using var crop = orientedSource.CloneCrop(pixels);
        await using var output = new MemoryStream();
        await crop.SaveAsPngAsync(output, cancellationToken);
        var content = output.ToArray();
        var original = source.Request;
        var stem = Path.GetFileNameWithoutExtension(original.FileName);

        return original with
        {
            Content = content,
            FileName = $"{stem}-{memberId}.png",
            ContentType = "image/png",
            FileSizeBytes = content.LongLength
        };
    }

    private static CaptureRejectedRegion Reject(
        DocumentRegionProposal proposal,
        NormalizedBounds? trustedBounds,
        string reason)
    {
        var target = $"{proposal.SourceItemId}.regions[{proposal.DetectionIndex}]";
        return new CaptureRejectedRegion(
            proposal.SourceItemId,
            proposal.DetectionIndex,
            proposal.Bounds,
            trustedBounds,
            new CaptureProcessingError(
                InvalidDetectedRegionCode,
                $"Detected region {proposal.DetectionIndex} {reason}",
                target));
    }

    private static CaptureRegionValidationOutput PassThroughFailure(
        CaptureWorkflowContext context,
        CompositeCaptureSource source,
        CaptureSourceDetectionOutput detection)
    {
        return new CaptureRegionValidationOutput(
            context,
            source,
            detection.ImageMetadata,
            [],
            [],
            detection.ModelUsage,
            detection.Errors,
            detection.Warnings);
    }

    private static CaptureRegionValidationOutput SourceFailure(
        CaptureWorkflowContext context,
        CompositeCaptureSource source,
        CaptureSourceDetectionOutput detection,
        string code,
        string message)
    {
        return new CaptureRegionValidationOutput(
            context,
            source,
            detection.ImageMetadata,
            [],
            [],
            detection.ModelUsage,
            [new CaptureProcessingError(code, message, source.SourceItemId)],
            detection.Warnings);
    }

    private sealed class RegionCandidate
    {
        public required DocumentRegionProposal Proposal { get; init; }

        public required NormalizedBounds Bounds { get; set; }

        public required PixelRectangle Pixels { get; set; }

        public IReadOnlyList<NormalizedPoint>? Outline { get; init; }

        public decimal? Confidence { get; init; }

        public List<string> Warnings { get; } = [];
    }
}

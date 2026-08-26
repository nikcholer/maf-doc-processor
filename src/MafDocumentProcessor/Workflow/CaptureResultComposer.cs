using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Workflow;

public static class CaptureResultComposer
{
    public const decimal AdvisoryConfidenceThreshold = 0.80m;
    public const string MemberLimitReason = "exceeds the configured member limit.";

    public static CompositeCaptureResult Compose(
        CompositeCaptureRequest request,
        IReadOnlyList<CaptureProcessedSource> sources,
        IReadOnlyList<CaptureMemberWorkflowOutcome> processedOutcomes,
        IReadOnlyList<CaptureRejectedRegion> additionalRejectedRegions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(processedOutcomes);
        ArgumentNullException.ThrowIfNull(additionalRejectedRegions);

        var outcomesByMemberId = processedOutcomes.ToDictionary(
            outcome => outcome.Member.Member.MemberId,
            StringComparer.Ordinal);
        var members = new List<CaptureMemberResult>();
        var sourceResults = new List<CaptureSourceResult>();
        var usageCalls = new List<ModelTokenUsage>();
        var captureErrors = new List<string>();
        var captureWarnings = new List<string>();
        var captureIndex = 1;

        foreach (var source in sources.OrderBy(item => item.Source.Index))
        {
            usageCalls.AddRange(source.DetectionUsage.Calls);
            var sourceMembers = new List<CaptureMemberResult>();
            foreach (var accepted in source.AcceptedMembers)
            {
                if (!outcomesByMemberId.TryGetValue(accepted.Member.MemberId, out var outcome))
                {
                    sourceMembers.Add(FromRejected(
                        accepted.Member.SourceItemId,
                        accepted.Member.Region.DetectionIndex,
                        accepted.Member.SourceMemberIndex,
                        captureIndex,
                        accepted.Member.Region,
                        new CaptureProcessingError(
                            CaptureRegionValidationService.InvalidDetectedRegionCode,
                            $"Detected region {accepted.Member.Region.DetectionIndex} {MemberLimitReason}",
                            accepted.Member.MemberId)));
                    captureIndex++;
                    continue;
                }

                var member = WithCaptureIndex(accepted.Member, captureIndex);
                sourceMembers.Add(FromOutcome(member, outcome));
                if (outcome.Result is not null)
                {
                    usageCalls.AddRange(outcome.Result.ModelUsage.Calls);
                }

                captureIndex++;
            }

            foreach (var rejected in source.RejectedRegions.Concat(
                additionalRejectedRegions.Where(region =>
                    string.Equals(region.SourceItemId, source.Source.SourceItemId, StringComparison.Ordinal))))
            {
                var sourceMemberIndex = sourceMembers.Count + 1;
                sourceMembers.Add(FromRejectedRegion(rejected, sourceMemberIndex, captureIndex));
                captureIndex++;
            }

            var sourceResult = ToSourceResult(source, sourceMembers);
            sourceResults.Add(sourceResult);
            members.AddRange(sourceMembers);
            captureErrors.AddRange(sourceResult.Errors);
            captureWarnings.AddRange(sourceResult.Warnings);
        }

        if (members.TrueForAll(member => member.Result is not { IsSuccess: true })
            && captureErrors.Count == 0)
        {
            captureErrors.Add("The capture did not produce a usable document region.");
        }

        return new CompositeCaptureResult(
            request.CaptureId,
            new CompositeCaptureMetadata(
                request.ReceivedAt,
                request.SourceId,
                request.Sources.Count,
                request.Sources.Sum(source => source.Request.FileSizeBytes)),
            sourceResults,
            DocumentModelUsage.FromCalls(usageCalls),
            Status(
                members,
                failedWithoutMembers: members.Count == 0,
                anyFailedSource: sourceResults.Any(source => source.Status == CaptureProcessingStatus.Failed)),
            members,
            captureErrors.Distinct(StringComparer.Ordinal).ToArray(),
            captureWarnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static CaptureMemberResult FromOutcome(
        CaptureMember member,
        CaptureMemberWorkflowOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            return new CaptureMemberResult(
                member,
                CaptureMemberStatus.Failed,
                CaptureMemberDisposition.Rejected,
                [outcome.Error.Message],
                result: null,
                outcome.Error);
        }

        var result = outcome.Result
            ?? throw new InvalidOperationException("A processed member outcome must include a result or an error.");
        var disposition = Disposition(member, result);
        return new CaptureMemberResult(
            member,
            CaptureMemberStatus.Processed,
            disposition.Disposition,
            disposition.Reasons,
            result,
            error: null);
    }

    public static CaptureProcessingError ToMemberError(Exception exception, string memberId)
    {
        return exception switch
        {
            DocumentModelResponseException ex => new CaptureProcessingError(
                "model_response_invalid",
                ex.Message,
                memberId),
            TimeoutException ex => new CaptureProcessingError(
                "model_timeout",
                ex.Message,
                memberId),
            ModelProviderException ex => new CaptureProcessingError(
                "model_provider_failed",
                ex.Message,
                memberId),
            _ => new CaptureProcessingError(
                "document_processing_failed",
                exception.Message,
                memberId)
        };
    }

    private static CaptureMemberResult FromRejectedRegion(
        CaptureRejectedRegion rejected,
        int sourceMemberIndex,
        int captureIndex)
    {
        var region = rejected.TrustedBounds is null
            ? new DetectedDocumentRegion(
                rejected.SourceItemId,
                rejected.DetectionIndex,
                new NormalizedBounds(0, 0, MinTrustedSize(), MinTrustedSize()),
                warnings: [rejected.Error.Message])
            : new DetectedDocumentRegion(
                rejected.SourceItemId,
                rejected.DetectionIndex,
                rejected.TrustedBounds,
                outline: null,
                confidence: null,
                warnings: [rejected.Error.Message]);
        return FromRejected(
            rejected.SourceItemId,
            rejected.DetectionIndex,
            sourceMemberIndex,
            captureIndex,
            region,
            rejected.Error);
    }

    private static CaptureMemberResult FromRejected(
        string sourceItemId,
        int detectionIndex,
        int sourceMemberIndex,
        int captureIndex,
        DetectedDocumentRegion region,
        CaptureProcessingError error)
    {
        var member = new CaptureMember(
            sourceItemId,
            CaptureIdentifiers.MemberId(sourceItemId, sourceMemberIndex),
            captureIndex,
            sourceMemberIndex,
            region);
        return new CaptureMemberResult(
            member,
            CaptureMemberStatus.Failed,
            CaptureMemberDisposition.Rejected,
            [error.Message],
            result: null,
            error);
    }

    private static CaptureMember WithCaptureIndex(CaptureMember member, int captureIndex)
    {
        return new CaptureMember(
            member.SourceItemId,
            member.MemberId,
            captureIndex,
            member.SourceMemberIndex,
            member.Region);
    }

    private static (CaptureMemberDisposition Disposition, IReadOnlyList<string> Reasons) Disposition(
        CaptureMember member,
        DocumentProcessingResult result)
    {
        var reasons = new List<string>();
        if (!result.IsSuccess)
        {
            reasons.AddRange(result.Errors);
            if (reasons.Count == 0)
            {
                reasons.Add("The document was not processed successfully.");
            }

            return (CaptureMemberDisposition.Rejected, reasons);
        }

        if (result.HumanReview.Status != HumanReviewStatus.NotRequired)
        {
            reasons.AddRange(result.HumanReview.Reasons);
        }

        reasons.AddRange(member.Region.Warnings);
        reasons.AddRange(result.Warnings);
        if (member.Region.Confidence is { } detectionConfidence
            && detectionConfidence < AdvisoryConfidenceThreshold)
        {
            reasons.Add(
                $"Detection confidence {detectionConfidence:0.00} is below normal processing threshold {AdvisoryConfidenceThreshold:0.00}.");
        }

        var distinctReasons = reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinctReasons.Length == 0
            ? (CaptureMemberDisposition.Accepted, [])
            : (CaptureMemberDisposition.Review, distinctReasons);
    }

    private static CaptureSourceResult ToSourceResult(
        CaptureProcessedSource source,
        IReadOnlyList<CaptureMemberResult> members)
    {
        var metadata = new CaptureSourceMetadata(
            source.Source.Request.FileName,
            source.Source.Request.ContentType,
            source.Source.Request.FileSizeBytes,
            source.Source.Request.ReceivedAt,
            source.ImageMetadata?.OrientedWidthPixels,
            source.ImageMetadata?.OrientedHeightPixels);
        var errors = source.Errors.Select(error => error.Message).ToArray();
        var acceptedCount = source.AcceptedMembers.Count;
        return new CaptureSourceResult(
            source.Source.SourceItemId,
            source.Source.Index,
            metadata,
            new CaptureDetectionSummary(
                source.DetectionUsage.Calls.FirstOrDefault()?.ModelId,
                source.ProposedRegionCount,
                acceptedCount,
                source.Warnings,
                usedRegionOverrides: source.Source.RegionOverrides is not null),
            Status(members, failedWithoutMembers: source.Errors.Count > 0 && members.Count == 0),
            source.DetectionUsage,
            errors,
            source.Warnings);
    }

    private static CaptureProcessingStatus Status(
        IReadOnlyList<CaptureMemberResult> members,
        bool failedWithoutMembers = false,
        bool anyFailedSource = false)
    {
        if (failedWithoutMembers || members.Count == 0)
        {
            return CaptureProcessingStatus.Failed;
        }

        var anySuccess = members.Any(member => member.Result is { IsSuccess: true });
        var anyFailure = anyFailedSource || members.Any(member =>
            member.Status == CaptureMemberStatus.Failed || member.Result is not { IsSuccess: true });
        if (anySuccess && !anyFailure)
        {
            return CaptureProcessingStatus.Succeeded;
        }

        return anySuccess
            ? CaptureProcessingStatus.PartiallySucceeded
            : CaptureProcessingStatus.Failed;
    }

    private static double MinTrustedSize()
    {
        return 0.02;
    }
}

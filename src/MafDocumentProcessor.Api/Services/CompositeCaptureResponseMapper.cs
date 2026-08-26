using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Services;

public static class CompositeCaptureResponseMapper
{
    public static CompositeCaptureProcessingResponse Map(
        CompositeCaptureResult result,
        string traceId)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);

        return new CompositeCaptureProcessingResponse(
            result.CaptureId,
            result.Metadata,
            result.Sources.Select(MapSource).ToArray(),
            result.ModelUsage,
            result.Status,
            result.Members.Select(member => MapMember(member, traceId)).ToArray(),
            result.Errors,
            result.Warnings);
    }

    private static CompositeCaptureSourceResponse MapSource(CaptureSourceResult source)
    {
        return new CompositeCaptureSourceResponse(
            source.SourceItemId,
            source.Index,
            source.Metadata,
            source.Detection,
            source.Status,
            source.Errors,
            source.Warnings);
    }

    private static CompositeCaptureMemberResponse MapMember(
        CaptureMemberResult member,
        string traceId)
    {
        var region = member.Member.Region;
        return new CompositeCaptureMemberResponse(
            member.Member.SourceItemId,
            member.Member.MemberId,
            member.Member.Index,
            new CaptureRegionResponse(
                member.Member.SourceItemId,
                member.Member.MemberId,
                member.Member.Index,
                region.Bounds,
                region.Outline,
                region.Confidence,
                region.Warnings),
            member.Status,
            member.Disposition,
            member.DispositionReasons,
            member.Result is null ? null : DocumentProcessingResponseMapper.Map(member.Result),
            member.Error is null
                ? null
                : new ApiErrorResponse(
                    member.Error.Code,
                    member.Error.Message,
                    member.Error.Target,
                    traceId));
    }
}

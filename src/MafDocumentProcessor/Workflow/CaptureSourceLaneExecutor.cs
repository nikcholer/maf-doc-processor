using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureSourceLaneExecutor(
    int laneIndex,
    int laneCount,
    ICaptureSourceDetectionService detectionService,
    ICaptureRegionValidationService validationService,
    CancellationToken workflowCancellationToken = default)
    : Executor<CaptureSourceWork, CaptureSourceLaneResult>(ExecutorId(laneIndex + 1))
{
    public static string ExecutorId(int oneBasedLane) => $"capture-source-lane-{oneBasedLane}";

    public override async ValueTask<CaptureSourceLaneResult> HandleAsync(
        CaptureSourceWork message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var token = linkedCancellation.Token;
        var assigned = CaptureLaneAssignment.ForLane(message.Request.Sources, laneIndex, laneCount);
        var processed = new List<CaptureProcessedSource>(assigned.Count);
        foreach (var source in assigned)
        {
            token.ThrowIfCancellationRequested();
            processed.Add(await ProcessSourceAsync(message.Context, source, context, token));
        }

        return new CaptureSourceLaneResult(message.Context, message.Request, laneIndex, processed);
    }

    private async Task<CaptureProcessedSource> ProcessSourceAsync(
        CaptureWorkflowContext captureContext,
        CompositeCaptureSource source,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var sourceContext = captureContext.ForSource(source.SourceItemId);
        using var detection = await detectionService.DetectAsync(
            new CaptureSourceDetectionInput(sourceContext, source),
            cancellationToken);
        var validation = await validationService.ValidateAsync(
            new CaptureRegionValidationInput(sourceContext, source, detection),
            cancellationToken);
        await context.AddEventAsync(
            new WorkflowEvent(new CaptureSourceCompletedEvent(
                sourceContext.TraceId,
                sourceContext.CaptureId,
                sourceContext.SourceId,
                source.SourceItemId,
                validation.IsSuccess,
                detection.Proposals.Count,
                validation.AcceptedMembers.Count,
                validation.Errors
                    .Concat(validation.RejectedRegions.Select(region => region.Error))
                    .Select(error => error.Code)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())),
            cancellationToken);

        return new CaptureProcessedSource(
            source,
            validation.ImageMetadata ?? detection.ImageMetadata,
            detection.Proposals.Count,
            detection.ModelUsage,
            validation.AcceptedMembers,
            validation.RejectedRegions,
            detection.Errors.Concat(validation.Errors).ToArray(),
            detection.Warnings.Concat(validation.Warnings).Distinct(StringComparer.Ordinal).ToArray());
    }
}

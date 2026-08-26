using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureSourceDetectionExecutor(
    ICaptureSourceDetectionService detectionService,
    CancellationToken workflowCancellationToken = default)
    : Executor<CaptureSourceDetectionInput, CaptureSourceDetectionOutput>(ExecutorId)
{
    public const string ExecutorId = "CaptureSourceDetection";

    public override async ValueTask<CaptureSourceDetectionOutput> HandleAsync(
        CaptureSourceDetectionInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var output = await detectionService.DetectAsync(message, linkedCancellation.Token);

        if (output.ImageMetadata is { } imageMetadata)
        {
            await context.AddEventAsync(
                new WorkflowEvent(new CaptureSourceDecodedEvent(
                    output.Context.TraceId,
                    output.Context.CaptureId,
                    output.Context.SourceId,
                    output.Source.SourceItemId,
                    imageMetadata.OriginalWidthPixels,
                    imageMetadata.OriginalHeightPixels,
                    imageMetadata.OrientedWidthPixels,
                    imageMetadata.OrientedHeightPixels)),
                linkedCancellation.Token);
        }

        await context.AddEventAsync(
            new WorkflowEvent(new CaptureSourceDetectionCompletedEvent(
                output.Context.TraceId,
                output.Context.CaptureId,
                output.Context.SourceId,
                output.Source.SourceItemId,
                output.IsSuccess,
                output.Proposals.Count,
                output.ModelUsage.Calls.SingleOrDefault()?.ModelId,
                output.Errors.Select(error => error.Code).ToArray(),
                UsedRegionOverrides: message.Source.RegionOverrides is not null)),
            linkedCancellation.Token);

        return output;
    }
}

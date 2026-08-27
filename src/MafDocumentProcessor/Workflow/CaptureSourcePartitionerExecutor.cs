using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureSourcePartitionerExecutor(CancellationToken workflowCancellationToken = default)
    : Executor<CompositeCaptureRequest, CaptureSourceWork>(ExecutorId)
{
    public const string ExecutorId = "capture-source-partitioner";

    public override async ValueTask<CaptureSourceWork> HandleAsync(
        CompositeCaptureRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var captureContext = new CaptureWorkflowContext(
            message.TraceId ?? ActivityId(context),
            message.CaptureId,
            message.SourceId);
        await context.AddEventAsync(
            new WorkflowEvent(new CaptureStartedEvent(
                captureContext.TraceId,
                captureContext.CaptureId,
                captureContext.SourceId,
                message.Sources.Count)),
            linkedCancellation.Token);
        return new CaptureSourceWork(captureContext, message);
    }

    private static string ActivityId(IWorkflowContext context)
    {
        if (context.TraceContext is { } trace
            && trace.TryGetValue("traceparent", out var traceParent)
            && !string.IsNullOrWhiteSpace(traceParent))
        {
            return traceParent;
        }

        return $"trace-{Guid.NewGuid():N}";
    }
}

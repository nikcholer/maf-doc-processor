using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

[YieldsOutput(typeof(CompositeCaptureResult))]
public sealed class CaptureMemberFanInExecutor()
    : Executor<CaptureMemberLaneResult>(ExecutorId)
{
    public const string ExecutorId = "capture-member-fan-in";

    private readonly List<CaptureMemberLaneResult> _lanes = [];

    public override ValueTask HandleAsync(
        CaptureMemberLaneResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _lanes.Add(message);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnMessageDeliveryFinishedAsync(
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var prototype = _lanes.OrderBy(lane => lane.LaneIndex).First();
        var outcomes = _lanes.SelectMany(lane => lane.Outcomes).ToArray();
        var result = CaptureResultComposer.Compose(
            prototype.Stage.Request,
            prototype.Stage.Sources,
            outcomes,
            prototype.Stage.AdditionalRejectedRegions);
        await context.AddEventAsync(
            new WorkflowEvent(new CaptureCompletedEvent(
                prototype.Context.TraceId,
                prototype.Context.CaptureId,
                prototype.Context.SourceId,
                result.Status.ToString(),
                result.Sources.Count,
                result.Members.Count)),
            cancellationToken);
        _lanes.Clear();
        await context.YieldOutputAsync(result, cancellationToken);
    }
}

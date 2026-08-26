using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

[YieldsOutput(typeof(CaptureSourceStageResult))]
public sealed class CaptureSourceFanInExecutor(int maxMembersPerCapture)
    : Executor<CaptureSourceLaneResult>(ExecutorId)
{
    public const string ExecutorId = "capture-source-fan-in";

    private readonly List<CaptureSourceLaneResult> _lanes = [];

    public override ValueTask HandleAsync(
        CaptureSourceLaneResult message,
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
        var sources = _lanes
            .SelectMany(lane => lane.Sources)
            .OrderBy(source => source.Source.Index)
            .ToArray();
        var accepted = sources
            .SelectMany(source => source.AcceptedMembers)
            .ToArray();
        var membersToProcess = accepted.Take(maxMembersPerCapture).ToArray();
        var stage = new CaptureSourceStageResult(
            prototype.Context,
            prototype.Request,
            sources,
            membersToProcess,
            []);
        await context.AddEventAsync(
            new WorkflowEvent(new CaptureSourcesAggregatedEvent(
                prototype.Context.TraceId,
                prototype.Context.CaptureId,
                prototype.Context.SourceId,
                sources.Length,
                membersToProcess.Length,
                sources.Sum(source => source.RejectedRegions.Count)
                    + Math.Max(0, accepted.Length - membersToProcess.Length))),
            cancellationToken);

        _lanes.Clear();
        await context.YieldOutputAsync(stage, cancellationToken);
    }
}

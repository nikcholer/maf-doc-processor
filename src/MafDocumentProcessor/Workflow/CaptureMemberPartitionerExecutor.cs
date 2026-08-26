using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureMemberPartitionerExecutor()
    : Executor<CaptureSourceStageResult, CaptureMemberWork>(ExecutorId)
{
    public const string ExecutorId = "capture-member-partitioner";

    public override ValueTask<CaptureMemberWork> HandleAsync(
        CaptureSourceStageResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CaptureMemberWork(
            message.Context,
            message,
            message.MembersToProcess));
    }
}

using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureMemberLaneExecutor(
    int laneIndex,
    int laneCount,
    DocumentProcessingWorkflow documentWorkflow,
    CancellationToken workflowCancellationToken = default)
    : Executor<CaptureMemberWork, CaptureMemberLaneResult>(ExecutorId(laneIndex + 1))
{
    public static string ExecutorId(int oneBasedLane) => $"capture-member-lane-{oneBasedLane}";

    public override async ValueTask<CaptureMemberLaneResult> HandleAsync(
        CaptureMemberWork message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var token = linkedCancellation.Token;
        var assigned = CaptureLaneAssignment.ForLane(message.Members, laneIndex, laneCount);
        var outcomes = new List<CaptureMemberWorkflowOutcome>(assigned.Count);
        foreach (var member in assigned)
        {
            token.ThrowIfCancellationRequested();
            outcomes.Add(await ProcessMemberAsync(member, context, token));
        }

        return new CaptureMemberLaneResult(message.Context, message.Stage, laneIndex, outcomes);
    }

    private async Task<CaptureMemberWorkflowOutcome> ProcessMemberAsync(
        CaptureMemberProcessingInput member,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        await context.AddEventAsync(
            new WorkflowEvent(new CaptureMemberStartedEvent(
                member.Context.TraceId,
                member.Context.CaptureId,
                member.Context.SourceId,
                member.Member.SourceItemId,
                member.Member.MemberId)),
            cancellationToken);
        CaptureMemberWorkflowOutcome outcome;
        try
        {
            var result = await documentWorkflow.RunAsync(member.CropRequest, cancellationToken);
            outcome = new CaptureMemberWorkflowOutcome(member, result, Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ModelConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome = new CaptureMemberWorkflowOutcome(
                member,
                Result: null,
                CaptureResultComposer.ToMemberError(ex, member.Member.MemberId));
        }

        await context.AddEventAsync(
            new WorkflowEvent(new CaptureMemberCompletedEvent(
                member.Context.TraceId,
                member.Context.CaptureId,
                member.Context.SourceId,
                member.Member.SourceItemId,
                member.Member.MemberId,
                outcome.Error is null && outcome.Result is { IsSuccess: true },
                outcome.Error is null
                    ? CaptureResultComposer.FromOutcome(member.Member, outcome).Disposition.ToString()
                    : CaptureMemberDisposition.Rejected.ToString())),
            cancellationToken);
        return outcome;
    }
}

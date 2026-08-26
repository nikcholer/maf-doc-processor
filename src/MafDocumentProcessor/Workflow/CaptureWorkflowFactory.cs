using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public static class CaptureWorkflowFactory
{
    public const string SourceWorkflowName = "Composite Capture Sources";
    public const string MemberWorkflowName = "Composite Capture Members";

    public static Microsoft.Agents.AI.Workflows.Workflow BuildSourceWorkflow(
        ICaptureSourceDetectionService detectionService,
        ICaptureRegionValidationService validationService,
        CompositeCaptureOptions captureOptions,
        CancellationToken cancellationToken = default)
    {
        var partitioner = new CaptureSourcePartitionerExecutor(cancellationToken);
        var lanes = Enumerable.Range(0, captureOptions.MaxConcurrentSources)
            .Select(index => (ExecutorBinding)new CaptureSourceLaneExecutor(
                index,
                captureOptions.MaxConcurrentSources,
                detectionService,
                validationService,
                cancellationToken))
            .ToArray();
        var fanIn = new CaptureSourceFanInExecutor(captureOptions.MaxMembersPerCapture);

        return new WorkflowBuilder(partitioner)
            .AddFanOutEdge(partitioner, lanes, "source-fan-out")
            .AddFanInBarrierEdge(lanes, fanIn, "source-fan-in")
            .WithOutputFrom(fanIn)
            .WithName(SourceWorkflowName)
            .WithDescription("Detects and crops document regions using a fixed set of source lanes.")
            .Build();
    }

    public static Microsoft.Agents.AI.Workflows.Workflow BuildMemberWorkflow(
        DocumentProcessingWorkflow documentWorkflow,
        CompositeCaptureOptions captureOptions,
        CancellationToken cancellationToken = default)
    {
        var partitioner = new CaptureMemberPartitionerExecutor();
        var lanes = Enumerable.Range(0, captureOptions.MaxConcurrentMembers)
            .Select(index => (ExecutorBinding)new CaptureMemberLaneExecutor(
                index,
                captureOptions.MaxConcurrentMembers,
                documentWorkflow,
                cancellationToken))
            .ToArray();
        var fanIn = new CaptureMemberFanInExecutor();

        return new WorkflowBuilder(partitioner)
            .AddFanOutEdge(partitioner, lanes, "member-fan-out")
            .AddFanInBarrierEdge(lanes, fanIn, "member-fan-in")
            .WithOutputFrom(fanIn)
            .WithName(MemberWorkflowName)
            .WithDescription("Processes accepted capture members through reusable document workflows.")
            .Build();
    }
}

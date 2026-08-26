using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class CaptureRegionValidationExecutor(
    ICaptureRegionValidationService validationService,
    CancellationToken workflowCancellationToken = default)
    : Executor<CaptureRegionValidationInput, CaptureRegionValidationOutput>(ExecutorId)
{
    public const string ExecutorId = "CaptureRegionValidation";

    public override async ValueTask<CaptureRegionValidationOutput> HandleAsync(
        CaptureRegionValidationInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        using (message.Detection)
        {
            var output = await validationService.ValidateAsync(message, linkedCancellation.Token);
            await context.AddEventAsync(
                new WorkflowEvent(new CaptureRegionValidationCompletedEvent(
                    output.Context.TraceId,
                    output.Context.CaptureId,
                    output.Context.SourceId,
                    output.Source.SourceItemId,
                    output.IsSuccess,
                    message.Detection.Proposals.Count,
                    output.AcceptedMembers.Count,
                    output.RejectedRegions.Count,
                    output.Errors
                        .Concat(output.RejectedRegions.Select(region => region.Error))
                        .Select(error => error.Code)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray())),
                linkedCancellation.Token);

            return output;
        }
    }
}

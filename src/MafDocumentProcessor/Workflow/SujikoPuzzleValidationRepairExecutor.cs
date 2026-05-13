using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class SujikoPuzzleValidationRepairExecutor(
    ISujikoPuzzleExtractor extractor,
    CancellationToken workflowCancellationToken = default)
    : Executor<ValidatedSujikoPuzzleExtraction, ValidatedSujikoPuzzleExtraction>("SujikoPuzzleValidationRepair")
{
    public override async ValueTask<ValidatedSujikoPuzzleExtraction> HandleAsync(
        ValidatedSujikoPuzzleExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Validation.IsValid)
        {
            return message;
        }

        var extraction = message.Extraction;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var repaired = await extractor.ExtractSujikoPuzzleAsync(
            extraction.ClassifiedDocument.Request,
            linkedCancellation.Token,
            message.Validation.Reasons);

        var repairedExtraction = extraction with
        {
            SujikoPuzzle = repaired.Value,
            ExtractionUsages = [.. extraction.ExtractionUsages, repaired.Usage]
        };

        return new ValidatedSujikoPuzzleExtraction(
            repairedExtraction,
            SujikoPuzzleValidationExecutor.Validate(repaired.Value));
    }
}

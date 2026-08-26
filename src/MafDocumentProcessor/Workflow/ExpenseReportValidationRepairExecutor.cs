using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ExpenseReportValidationRepairExecutor(
    IExpenseReportExtractor extractor,
    CancellationToken workflowCancellationToken = default)
    : Executor<ValidatedExpenseReportExtraction, ValidatedExpenseReportExtraction>(
        "ExpenseReportValidationRepair")
{
    public override async ValueTask<ValidatedExpenseReportExtraction> HandleAsync(
        ValidatedExpenseReportExtraction message,
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
        var repaired = await extractor.ExtractExpenseReportAsync(
            extraction.ClassifiedDocument.Request,
            linkedCancellation.Token,
            message.Validation.Reasons);

        var repairedExtraction = extraction with
        {
            ExpenseReport = repaired.Value,
            ExtractionUsages = [.. extraction.ExtractionUsages, repaired.Usage]
        };

        return new ValidatedExpenseReportExtraction(
            repairedExtraction,
            ExpenseReportValidationExecutor.Validate(repaired.Value));
    }
}

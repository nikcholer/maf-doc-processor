using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ExpenseReportExtractionExecutor(
    IExpenseReportExtractor extractor,
    CancellationToken workflowCancellationToken = default)
    : Executor<ClassifiedDocument, ExpenseReportExtraction>("ExpenseReportExtraction")
{
    public override async ValueTask<ExpenseReportExtraction> HandleAsync(
        ClassifiedDocument message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.Classification.Category != DocumentCategory.ExpenseReport)
        {
            throw new InvalidOperationException(
                $"Expense report extraction received a {message.Classification.Category} document.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var extraction = await extractor.ExtractExpenseReportAsync(
            message.Request,
            linkedCancellation.Token);
        return new ExpenseReportExtraction(
            message,
            extraction.Value,
            extraction.Usage);
    }
}

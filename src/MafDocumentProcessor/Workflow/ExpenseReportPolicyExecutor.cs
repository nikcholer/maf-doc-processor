using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ExpenseReportPolicyExecutor(ExpensePolicyOptions options)
    : Executor<ValidatedExpenseReportExtraction, ExpenseReportPolicyEvaluation>("ExpenseReportPolicy")
{
    public const string HighValueReviewReason = "high-value expense requires review";
    public const string MissingReceiptReferenceReason = "receipt reference is not visible";

    public override ValueTask<ExpenseReportPolicyEvaluation> HandleAsync(
        ValidatedExpenseReportExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var report = message.Extraction.ExpenseReport;
        var reasons = new List<string>();
        var isWithinHighValueThreshold =
            report.ClaimedTotal <= options.HighValueReviewThreshold
            && report.Lines.All(line => line.Amount <= options.HighValueReviewThreshold);
        var allLinesHaveReceiptReferences = report.Lines.Count > 0
            && report.Lines.All(line => !string.IsNullOrWhiteSpace(line.ReceiptReference));

        if (!isWithinHighValueThreshold)
        {
            reasons.Add(HighValueReviewReason);
        }

        if (!allLinesHaveReceiptReferences)
        {
            reasons.Add(MissingReceiptReferenceReason);
        }

        var decision = isWithinHighValueThreshold && allLinesHaveReceiptReferences
            ? PolicyDecision.Approved
            : PolicyDecision.NeedsReview;

        return ValueTask.FromResult(new ExpenseReportPolicyEvaluation(
            message,
            new ExpensePolicyResult(
                isWithinHighValueThreshold,
                allLinesHaveReceiptReferences,
                decision,
                reasons)));
    }
}

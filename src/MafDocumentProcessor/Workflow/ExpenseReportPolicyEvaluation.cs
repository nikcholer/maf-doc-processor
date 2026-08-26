using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ExpenseReportPolicyEvaluation(
    ValidatedExpenseReportExtraction ValidatedExtraction,
    ExpensePolicyResult PolicyResult);

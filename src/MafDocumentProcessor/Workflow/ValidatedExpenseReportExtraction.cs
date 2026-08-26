using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ValidatedExpenseReportExtraction(
    ExpenseReportExtraction Extraction,
    ValidationResult Validation);

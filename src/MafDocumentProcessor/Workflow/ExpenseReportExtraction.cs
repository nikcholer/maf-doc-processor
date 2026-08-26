using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ExpenseReportExtraction(
    ClassifiedDocument ClassifiedDocument,
    ExpenseReportData ExpenseReport,
    IReadOnlyList<ModelTokenUsage> ExtractionUsages)
{
    public ExpenseReportExtraction(
        ClassifiedDocument classifiedDocument,
        ExpenseReportData expenseReport,
        ModelTokenUsage extractionUsage)
        : this(classifiedDocument, expenseReport, [extractionUsage])
    {
    }
}

using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IExpenseReportExtractor
{
    ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
        FileRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null);
}
using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ExpenseReportValidationExecutor()
    : Executor<ExpenseReportExtraction, ValidatedExpenseReportExtraction>("ExpenseReportValidation")
{
    public const string ArithmeticMismatchReason =
        "claimed total does not equal the sum of line amounts";
    public const decimal ArithmeticTolerance = 0.01m;

    public override ValueTask<ValidatedExpenseReportExtraction> HandleAsync(
        ExpenseReportExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            new ValidatedExpenseReportExtraction(message, Validate(message.ExpenseReport)));
    }

    public static ValidationResult Validate(ExpenseReportData report)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(report.ReportNumber)
            && string.IsNullOrWhiteSpace(report.Title))
        {
            reasons.Add("Expense report number or title is missing.");
        }

        if (report.CurrencyCode is not { Length: 3 })
        {
            reasons.Add("Expense report currency code must be a three-letter ISO-4217 code.");
        }

        if (report.ClaimedTotal < 0)
        {
            reasons.Add("Expense report claimed total cannot be negative.");
        }

        if (report.PeriodStart is { } start
            && report.PeriodEnd is { } end
            && start > end)
        {
            reasons.Add("Expense report period start must be on or before the period end.");
        }

        var lines = report.Lines;
        if (lines.Count == 0)
        {
            reasons.Add("Expense report contains no readable expense lines.");
        }

        if (lines.Any(line => string.IsNullOrWhiteSpace(line.Description)))
        {
            reasons.Add("Expense report contains a line without a readable description.");
        }

        if (lines.Any(line => line.Amount < 0))
        {
            reasons.Add("Expense report line amounts cannot be negative.");
        }

        if (HasDuplicateLines(lines))
        {
            reasons.Add("Expense report contains duplicate expense lines.");
        }

        if (report.PeriodStart is { } periodStart
            && report.PeriodEnd is { } periodEnd
            && lines.Any(line =>
                line.Date is { } lineDate
                && (lineDate < periodStart || lineDate > periodEnd)))
        {
            reasons.Add("Expense report contains a line date outside the reporting period.");
        }

        if (lines.Count > 0
            && Math.Abs(lines.Sum(line => line.Amount) - report.ClaimedTotal) > ArithmeticTolerance)
        {
            reasons.Add(ArithmeticMismatchReason);
        }

        return reasons.Count == 0
            ? ValidationResult.Valid
            : new ValidationResult(false, reasons);
    }

    private static bool HasDuplicateLines(IReadOnlyList<ExpenseReportLine> lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var key = string.Join(
                "|",
                line.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                line.Description.Trim(),
                line.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                line.ReceiptReference?.Trim() ?? "");
            if (!seen.Add(key))
            {
                return true;
            }
        }

        return false;
    }
}

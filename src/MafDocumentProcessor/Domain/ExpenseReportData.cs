namespace MafDocumentProcessor.Domain;

public sealed record ExpenseReportData(
    string? ReportNumber,
    string? Title,
    string? ClaimantName,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string CurrencyCode,
    decimal ClaimedTotal,
    IReadOnlyList<ExpenseReportLine> Lines,
    string? Notes,
    string? VisibleApprovalStatus);

public sealed record ExpenseReportLine(
    DateOnly? Date,
    string Description,
    string? Category,
    decimal Amount,
    string? ReceiptReference);

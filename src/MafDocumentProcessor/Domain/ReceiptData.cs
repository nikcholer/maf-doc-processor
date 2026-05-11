namespace MafDocumentProcessor.Domain;

public sealed record ReceiptData(
    string StoreName,
    decimal TotalAmount,
    DateOnly? PurchaseDate,
    string? PaymentMethod,
    string? CurrencyCode);

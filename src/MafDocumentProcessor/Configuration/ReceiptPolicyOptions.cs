namespace MafDocumentProcessor.Configuration;

public sealed record ReceiptPolicyOptions(
    decimal ReviewThreshold = 50m,
    string DefaultCurrencyCode = "GBP");

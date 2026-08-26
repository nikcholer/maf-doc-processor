namespace MafDocumentProcessor.Configuration;

public sealed record ExpensePolicyOptions(
    decimal HighValueReviewThreshold = 250m);

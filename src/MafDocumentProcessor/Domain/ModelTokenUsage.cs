namespace MafDocumentProcessor.Domain;

public sealed record ModelTokenUsage(
    string Operation,
    string ModelId,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    decimal? InputTokenPricePerMillionUsd = null,
    decimal? OutputTokenPricePerMillionUsd = null,
    decimal? EstimatedInputCostUsd = null,
    decimal? EstimatedOutputCostUsd = null,
    decimal? EstimatedTotalCostUsd = null);

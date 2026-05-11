namespace MafDocumentProcessor.Domain;

public sealed record ModelTokenUsage(
    string Operation,
    string ModelId,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens);

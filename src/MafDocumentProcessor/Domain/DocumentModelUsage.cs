namespace MafDocumentProcessor.Domain;

public sealed record DocumentModelUsage(
    IReadOnlyList<ModelTokenUsage> Calls,
    int? TotalInputTokens,
    int? TotalOutputTokens,
    int? TotalTokens,
    decimal? EstimatedInputCostUsd = null,
    decimal? EstimatedOutputCostUsd = null,
    decimal? EstimatedTotalCostUsd = null,
    long? TotalDurationMilliseconds = null)
{
    public static DocumentModelUsage FromCalls(IReadOnlyList<ModelTokenUsage> calls)
    {
        return new DocumentModelUsage(
            calls,
            SumKnownValues(calls.Select(call => call.InputTokens)),
            SumKnownValues(calls.Select(call => call.OutputTokens)),
            SumKnownValues(calls.Select(call => call.TotalTokens)),
            SumKnownValues(calls.Select(call => call.EstimatedInputCostUsd)),
            SumKnownValues(calls.Select(call => call.EstimatedOutputCostUsd)),
            SumKnownValues(calls.Select(call => call.EstimatedTotalCostUsd)),
            SumKnownValues(calls.Select(call => call.DurationMilliseconds)));
    }

    private static int? SumKnownValues(IEnumerable<int?> values)
    {
        var knownValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return knownValues.Length == 0 ? null : knownValues.Sum();
    }

    private static decimal? SumKnownValues(IEnumerable<decimal?> values)
    {
        var knownValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return knownValues.Length == 0 ? null : knownValues.Sum();
    }

    private static long? SumKnownValues(IEnumerable<long?> values)
    {
        var knownValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return knownValues.Length == 0 ? null : knownValues.Sum();
    }
}

namespace MafDocumentProcessor.Domain;

public sealed record DocumentModelUsage(
    IReadOnlyList<ModelTokenUsage> Calls,
    int? TotalInputTokens,
    int? TotalOutputTokens,
    int? TotalTokens)
{
    public static DocumentModelUsage FromCalls(IReadOnlyList<ModelTokenUsage> calls)
    {
        return new DocumentModelUsage(
            calls,
            SumKnownValues(calls.Select(call => call.InputTokens)),
            SumKnownValues(calls.Select(call => call.OutputTokens)),
            SumKnownValues(calls.Select(call => call.TotalTokens)));
    }

    private static int? SumKnownValues(IEnumerable<int?> values)
    {
        var knownValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return knownValues.Length == 0 ? null : knownValues.Sum();
    }
}

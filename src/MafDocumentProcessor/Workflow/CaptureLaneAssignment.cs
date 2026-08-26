namespace MafDocumentProcessor.Workflow;

public static class CaptureLaneAssignment
{
    public static IReadOnlyList<T> ForLane<T>(IReadOnlyList<T> items, int laneIndex, int laneCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(laneIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(laneCount, 1);
        if (laneIndex >= laneCount)
        {
            throw new ArgumentOutOfRangeException(nameof(laneIndex), laneIndex, "The lane index must be less than the lane count.");
        }

        return items
            .Where((_, index) => index % laneCount == laneIndex)
            .ToArray();
    }
}

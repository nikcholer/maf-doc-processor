namespace MafDocumentProcessor.Configuration;

public sealed record CompositeCaptureOptions(
    int MaxSourceCount = 5,
    long MaxSourceBytes = 10 * 1024 * 1024,
    long MaxAggregateBytes = 25 * 1024 * 1024,
    int MaxSourceWidthPixels = 12_000,
    int MaxSourceHeightPixels = 12_000,
    long MaxSourcePixelCount = 50_000_000,
    int MaxDetectedRegionsPerSource = 20,
    int MaxMembersPerCapture = 30,
    double MinRegionWidth = 0.02,
    double MinRegionHeight = 0.02,
    double MinRegionArea = 0.0025,
    double DuplicateIntersectionOverUnionThreshold = 0.90,
    double OverlapReviewIntersectionOverUnionThreshold = 0.10,
    int MaxConcurrentSources = 2,
    int MaxConcurrentMembers = 4)
{
    public CompositeCaptureOptions Validate()
    {
        RequirePositive(MaxSourceCount, nameof(MaxSourceCount));
        RequirePositive(MaxSourceBytes, nameof(MaxSourceBytes));
        RequirePositive(MaxAggregateBytes, nameof(MaxAggregateBytes));
        RequirePositive(MaxSourceWidthPixels, nameof(MaxSourceWidthPixels));
        RequirePositive(MaxSourceHeightPixels, nameof(MaxSourceHeightPixels));
        RequirePositive(MaxSourcePixelCount, nameof(MaxSourcePixelCount));
        RequirePositive(MaxDetectedRegionsPerSource, nameof(MaxDetectedRegionsPerSource));
        RequirePositive(MaxMembersPerCapture, nameof(MaxMembersPerCapture));
        RequireUnitInterval(MinRegionWidth, nameof(MinRegionWidth), allowZero: false);
        RequireUnitInterval(MinRegionHeight, nameof(MinRegionHeight), allowZero: false);
        RequireUnitInterval(MinRegionArea, nameof(MinRegionArea), allowZero: false);
        RequireUnitInterval(
            DuplicateIntersectionOverUnionThreshold,
            nameof(DuplicateIntersectionOverUnionThreshold),
            allowZero: false);
        RequireUnitInterval(
            OverlapReviewIntersectionOverUnionThreshold,
            nameof(OverlapReviewIntersectionOverUnionThreshold),
            allowZero: true);
        RequirePositive(MaxConcurrentSources, nameof(MaxConcurrentSources));
        RequirePositive(MaxConcurrentMembers, nameof(MaxConcurrentMembers));

        if (OverlapReviewIntersectionOverUnionThreshold >= DuplicateIntersectionOverUnionThreshold)
        {
            throw Invalid(
                nameof(OverlapReviewIntersectionOverUnionThreshold),
                "must be lower than DuplicateIntersectionOverUnionThreshold");
        }

        return this;
    }

    private static void RequirePositive(long value, string propertyName)
    {
        if (value <= 0)
        {
            throw Invalid(propertyName, "must be greater than zero");
        }
    }

    private static void RequireUnitInterval(double value, string propertyName, bool allowZero)
    {
        var lowerBoundIsInvalid = allowZero ? value < 0 : value <= 0;
        if (!double.IsFinite(value) || lowerBoundIsInvalid || value > 1)
        {
            var range = allowZero ? "between zero and one" : "greater than zero and no more than one";
            throw Invalid(propertyName, $"must be finite and {range}");
        }
    }

    private static CompositeCaptureConfigurationException Invalid(
        string propertyName,
        string requirement)
    {
        return new CompositeCaptureConfigurationException(
            $"CompositeCapture:{propertyName} {requirement}.");
    }
}

public sealed class CompositeCaptureConfigurationException(string message) : Exception(message);

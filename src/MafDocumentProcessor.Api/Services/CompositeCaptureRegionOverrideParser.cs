using System.Text.Json;
using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Services;

public static class CompositeCaptureRegionOverrideParser
{
    public const int MaxRegionSourceIdLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CaptureRegionOverrideParseResult Parse(
        string? json,
        int sourceCount,
        CompositeCaptureOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceCount);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(json))
        {
            return CaptureRegionOverrideParseResult.Success(Overrides: null);
        }

        CompositeCaptureRegionOverridesRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CompositeCaptureRegionOverridesRequest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return CaptureRegionOverrideParseResult.Failure(
                $"Region overrides must be valid JSON: {ex.Message}");
        }

        if (request?.Sources is null)
        {
            return CaptureRegionOverrideParseResult.Failure(
                "Region overrides must contain a 'sources' array.");
        }

        var overrides = new Dictionary<int, IReadOnlyList<CaptureRegionOverride>>();
        foreach (var source in request.Sources)
        {
            if (source is null)
            {
                return CaptureRegionOverrideParseResult.Failure(
                    "Every region override source entry must be an object.");
            }

            if (source.SourceIndex <= 0 || source.SourceIndex > sourceCount)
            {
                return CaptureRegionOverrideParseResult.Failure(
                    $"Region override sourceIndex must be between 1 and {sourceCount}.");
            }

            if (!overrides.TryAdd(source.SourceIndex, []))
            {
                return CaptureRegionOverrideParseResult.Failure(
                    $"Region override sourceIndex {source.SourceIndex} appears more than once.");
            }

            if (source.Regions is null)
            {
                return CaptureRegionOverrideParseResult.Failure(
                    $"Region override source {source.SourceIndex} must contain a 'regions' array.");
            }

            if (source.Regions.Count > options.MaxDetectedRegionsPerSource)
            {
                return CaptureRegionOverrideParseResult.Failure(
                    $"Region override source {source.SourceIndex} may contain at most {options.MaxDetectedRegionsPerSource} regions.");
            }

            var sourceOverrides = new List<CaptureRegionOverride>(source.Regions.Count);
            foreach (var region in source.Regions)
            {
                if (region?.Bounds is null)
                {
                    return CaptureRegionOverrideParseResult.Failure(
                        $"Every region override for source {source.SourceIndex} must contain bounds.");
                }

                if (region.Outline is { Count: not 4 })
                {
                    return CaptureRegionOverrideParseResult.Failure(
                        $"A region override outline for source {source.SourceIndex} must contain exactly four points.");
                }

                var sourceId = NormalizeRegionSourceId(region.SourceId);
                if (sourceId is { Length: > MaxRegionSourceIdLength })
                {
                    return CaptureRegionOverrideParseResult.Failure(
                        $"A region sourceId may contain at most {MaxRegionSourceIdLength} characters after trimming.");
                }

                if (sourceId?.Any(char.IsControl) == true)
                {
                    return CaptureRegionOverrideParseResult.Failure(
                        "A region sourceId cannot contain control characters.");
                }

                sourceOverrides.Add(new CaptureRegionOverride(region.Bounds, region.Outline, sourceId));
            }

            overrides[source.SourceIndex] = Array.AsReadOnly(sourceOverrides.ToArray());
        }

        return CaptureRegionOverrideParseResult.Success(overrides);
    }

    private static string? NormalizeRegionSourceId(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

public sealed record CaptureRegionOverrideParseResult(
    bool IsSuccess,
    IReadOnlyDictionary<int, IReadOnlyList<CaptureRegionOverride>>? Overrides,
    string? Error)
{
    public static CaptureRegionOverrideParseResult Success(
        IReadOnlyDictionary<int, IReadOnlyList<CaptureRegionOverride>>? Overrides)
    {
        return new CaptureRegionOverrideParseResult(true, Overrides, Error: null);
    }

    public static CaptureRegionOverrideParseResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CaptureRegionOverrideParseResult(false, Overrides: null, error);
    }
}

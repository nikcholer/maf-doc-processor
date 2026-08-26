using System.Text.Json;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public static class DocumentRegionResponseParser
{
    public static IReadOnlyList<DocumentRegionProposal> Parse(
        string? content,
        string sourceItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DocumentModelResponseException(
                "The document region detection model returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(NormalizeJsonObject(content));
            var root = document.RootElement;
            if (!root.TryGetProperty("regions", out var regions)
                || regions.ValueKind != JsonValueKind.Array)
            {
                throw new DocumentModelResponseException(
                    "The document region detection response did not include a 'regions' array.");
            }

            return regions
                .EnumerateArray()
                .Select((region, index) => ParseRegion(region, sourceItemId, index + 1))
                .ToArray();
        }
        catch (DocumentModelResponseException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new DocumentModelResponseException(
                $"The document region detection model returned invalid JSON. Response preview: {CreatePreview(content)}",
                ex);
        }
    }

    private static DocumentRegionProposal ParseRegion(
        JsonElement region,
        string sourceItemId,
        int detectionIndex)
    {
        if (region.ValueKind != JsonValueKind.Object
            || !region.TryGetProperty("bounds", out var bounds)
            || bounds.ValueKind != JsonValueKind.Object)
        {
            throw new DocumentModelResponseException(
                $"Detected region {detectionIndex} did not include a bounds object.");
        }

        return new DocumentRegionProposal(
            sourceItemId,
            detectionIndex,
            new ProposedNormalizedBounds(
                GetRequiredDouble(bounds, "x", detectionIndex),
                GetRequiredDouble(bounds, "y", detectionIndex),
                GetRequiredDouble(bounds, "width", detectionIndex),
                GetRequiredDouble(bounds, "height", detectionIndex)),
            ParseOutline(region, detectionIndex),
            GetOptionalDecimal(region, "confidence", detectionIndex));
    }

    private static IReadOnlyList<ProposedNormalizedPoint>? ParseOutline(
        JsonElement region,
        int detectionIndex)
    {
        if (!region.TryGetProperty("outline", out var outline)
            || outline.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (outline.ValueKind != JsonValueKind.Array || outline.GetArrayLength() != 4)
        {
            throw new DocumentModelResponseException(
                $"Detected region {detectionIndex} outline must contain exactly four points.");
        }

        return outline
            .EnumerateArray()
            .Select((point, pointIndex) => new ProposedNormalizedPoint(
                GetRequiredDouble(point, "x", detectionIndex, pointIndex + 1),
                GetRequiredDouble(point, "y", detectionIndex, pointIndex + 1)))
            .ToArray();
    }

    private static double GetRequiredDouble(
        JsonElement element,
        string propertyName,
        int detectionIndex,
        int? pointIndex = null)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var value))
        {
            var target = pointIndex.HasValue
                ? $"outline point {pointIndex.Value}"
                : "bounds";
            throw new DocumentModelResponseException(
                $"Detected region {detectionIndex} {target} did not include numeric '{propertyName}'.");
        }

        return value;
    }

    private static decimal? GetOptionalDecimal(
        JsonElement element,
        string propertyName,
        int detectionIndex)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value))
        {
            return value;
        }

        throw new DocumentModelResponseException(
            $"Detected region {detectionIndex} confidence was not numeric.");
    }

    private static string NormalizeJsonObject(string content)
    {
        var value = content.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = value.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                value = value[(firstLineBreak + 1)..];
            }

            var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                value = value[..closingFence];
            }
        }

        var objectStart = value.IndexOf('{');
        var objectEnd = value.LastIndexOf('}');
        return objectStart >= 0 && objectEnd > objectStart
            ? value[objectStart..(objectEnd + 1)]
            : value;
    }

    private static string CreatePreview(string content)
    {
        var preview = content.ReplaceLineEndings(" ").Trim();
        return preview.Length > 240 ? $"{preview[..240]}..." : preview;
    }
}

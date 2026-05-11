using System.Globalization;
using System.Text.Json;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public static class ModelResponseParsers
{
    public static DocumentClassification ParseClassification(string? content)
    {
        using var document = ParseJson(content, "classification");
        var root = document.RootElement;

        var categoryText = GetRequiredString(root, "category", "classification");
        if (!Enum.TryParse<DocumentCategory>(categoryText, ignoreCase: true, out var category))
        {
            throw new DocumentModelResponseException(
                $"The classification model returned unsupported category '{categoryText}'.");
        }

        var confidence = NormalizeConfidence(GetOptionalDecimal(root, "confidence"));
        var confidenceReasoning = GetOptionalString(root, "confidenceReasoning")
            ?? GetOptionalString(root, "reasoning")
            ?? "The model did not provide a confidence explanation.";

        return new DocumentClassification(category, confidence, confidenceReasoning);
    }

    public static ReceiptData ParseReceipt(string? content)
    {
        using var document = ParseJson(content, "receipt extraction");
        var root = document.RootElement;

        return new ReceiptData(
            GetRequiredString(root, "storeName", "receipt extraction"),
            GetRequiredDecimal(root, "totalAmount", "receipt extraction"),
            GetOptionalDate(root, "purchaseDate"),
            GetOptionalString(root, "paymentMethod"),
            NormalizeCurrencyCode(GetOptionalString(root, "currencyCode")));
    }

    private static JsonDocument ParseJson(string? content, string operation)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DocumentModelResponseException(
                $"The {operation} model returned an empty response.");
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new DocumentModelResponseException(
                $"The {operation} model returned invalid JSON.",
                ex);
        }
    }

    private static string GetRequiredString(JsonElement root, string propertyName, string operation)
    {
        var value = GetOptionalString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DocumentModelResponseException(
                $"The {operation} model response did not include '{propertyName}'.");
        }

        return value;
    }

    private static decimal GetRequiredDecimal(JsonElement root, string propertyName, string operation)
    {
        var value = GetOptionalDecimal(root, propertyName);
        if (value is null)
        {
            throw new DocumentModelResponseException(
                $"The {operation} model response did not include a valid '{propertyName}'.");
        }

        return value.Value;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static decimal? GetOptionalDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetDecimal(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static DateOnly? GetOptionalDate(JsonElement root, string propertyName)
    {
        var value = GetOptionalString(root, propertyName);
        if (value is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static decimal? NormalizeConfidence(decimal? confidence)
    {
        return confidence is >= 0 and <= 1 ? confidence : null;
    }

    private static string? NormalizeCurrencyCode(string? value)
    {
        return value is null ? null : value.Trim().ToUpperInvariant();
    }
}

using System.Globalization;
using System.Text.Json;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public static class ModelResponseParsers
{
    public static DocumentClassification ParseClassification(string? content)
    {
        if (TryParsePlainTextClassification(content, out var plainTextClassification))
        {
            return plainTextClassification;
        }

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
        var documentTypeDescription = GetOptionalString(root, "documentTypeDescription")
            ?? GetOptionalString(root, "documentType")
            ?? GetOptionalString(root, "apparentDocumentType");

        return new DocumentClassification(
            category,
            confidence,
            confidenceReasoning,
            documentTypeDescription);
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

    public static ShoppingListData ParseShoppingList(string? content)
    {
        using var document = ParseJson(content, "shopping list extraction");
        var root = document.RootElement;

        return new ShoppingListData(
            GetOptionalString(root, "title"),
            GetShoppingListItems(root),
            GetOptionalString(root, "notes"));
    }

    public static SujikoPuzzleData ParseSujikoPuzzle(string? content)
    {
        using var document = ParseJson(content, "Sujiko puzzle extraction");
        var root = document.RootElement;

        return new SujikoPuzzleData(
            GetSujikoQuadrantTotals(root),
            GetSujikoGivenCells(root));
    }

    public static ExpenseReportData ParseExpenseReport(string? content)
    {
        using var document = ParseJson(content, "expense report extraction");
        var root = document.RootElement;
        var currencyCode = NormalizeCurrencyCode(
            GetRequiredString(root, "currencyCode", "expense report extraction"));

        return new ExpenseReportData(
            GetOptionalString(root, "reportNumber"),
            GetOptionalString(root, "title"),
            GetOptionalString(root, "claimantName")
                ?? GetOptionalString(root, "employeeName"),
            GetOptionalDate(root, "periodStart"),
            GetOptionalDate(root, "periodEnd"),
            currencyCode ?? throw new DocumentModelResponseException(
                "The expense report extraction model response did not include a valid 'currencyCode'."),
            GetRequiredDecimal(root, "claimedTotal", "expense report extraction"),
            GetExpenseReportLines(root),
            GetOptionalString(root, "notes"),
            GetOptionalString(root, "visibleApprovalStatus")
                ?? GetOptionalString(root, "approvalStatus"));
    }

    private static JsonDocument ParseJson(string? content, string operation)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DocumentModelResponseException(
                $"The {operation} model returned an empty response.");
        }

        var json = NormalizeJsonObject(content);

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DocumentModelResponseException(
                $"The {operation} model returned invalid JSON. Response preview: {CreatePreview(content)}",
                ex);
        }
    }

    private static bool TryParsePlainTextClassification(
        string? content,
        out DocumentClassification classification)
    {
        classification = default!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var value = content.Trim().Trim('"', '\'', '.', ':').Trim();
        if (value.Contains('{', StringComparison.Ordinal)
            || value.Contains('}', StringComparison.Ordinal))
        {
            return false;
        }

        var category = value switch
        {
            _ when ContainsAny(value, "sujiko") => DocumentCategory.SujikoPuzzle,
            _ when ContainsAny(value, "shopping list", "grocery list", "packing list", "to-buy list") => DocumentCategory.ShoppingList,
            _ when ContainsAny(value, "expense report", "expense claim") => DocumentCategory.ExpenseReport,
            _ when value.Contains("receipt", StringComparison.OrdinalIgnoreCase) => DocumentCategory.Receipt,
            _ when value.Contains("invoice", StringComparison.OrdinalIgnoreCase) => DocumentCategory.Invoice,
            _ => (DocumentCategory?)null
        };

        if (category is null)
        {
            return false;
        }

        classification = new DocumentClassification(
            category.Value,
            Confidence: null,
            ConfidenceReasoning: "The model returned a plain-text document type description.",
            DocumentTypeDescription: value);
        return true;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
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

            value = value.Trim();
        }

        var objectStart = value.IndexOf('{');
        var objectEnd = value.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            value = value[objectStart..(objectEnd + 1)];
        }

        return value;
    }

    private static string CreatePreview(string content)
    {
        var preview = content.ReplaceLineEndings(" ").Trim();
        if (preview.Length == 0)
        {
            return "(empty response)";
        }

        return preview.Length > 240 ? $"{preview[..240]}..." : preview;
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

    private static int GetRequiredInt(JsonElement root, string propertyName, string operation)
    {
        var value = GetOptionalInt(root, propertyName);
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

    private static int? GetOptionalInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
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

    private static IReadOnlyList<ShoppingListItem> GetShoppingListItems(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var itemsProperty)
            || itemsProperty.ValueKind != JsonValueKind.Array)
        {
            throw new DocumentModelResponseException(
                "The shopping list extraction model response did not include an 'items' array.");
        }

        var items = new List<ShoppingListItem>();
        foreach (var itemProperty in itemsProperty.EnumerateArray())
        {
            var name = GetOptionalString(itemProperty, "name")
                ?? GetOptionalString(itemProperty, "item");

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new ShoppingListItem(
                name,
                GetOptionalDecimal(itemProperty, "quantity"),
                GetOptionalString(itemProperty, "unit"),
                GetOptionalBool(itemProperty, "isChecked")
                    ?? GetOptionalBool(itemProperty, "checked")));
        }

        return items;
    }

    private static SujikoQuadrantTotals GetSujikoQuadrantTotals(JsonElement root)
    {
        if (!root.TryGetProperty("quadrantTotals", out var totals)
            || totals.ValueKind != JsonValueKind.Object)
        {
            throw new DocumentModelResponseException(
                "The Sujiko puzzle extraction model response did not include a 'quadrantTotals' object.");
        }

        return new SujikoQuadrantTotals(
            GetRequiredInt(totals, "topLeft", "Sujiko puzzle extraction"),
            GetRequiredInt(totals, "topRight", "Sujiko puzzle extraction"),
            GetRequiredInt(totals, "bottomLeft", "Sujiko puzzle extraction"),
            GetRequiredInt(totals, "bottomRight", "Sujiko puzzle extraction"));
    }

    private static IReadOnlyList<SujikoCellValue> GetSujikoGivenCells(JsonElement root)
    {
        if (!root.TryGetProperty("givenCells", out var cellsProperty)
            && !root.TryGetProperty("cells", out cellsProperty))
        {
            return [];
        }

        if (cellsProperty.ValueKind != JsonValueKind.Array)
        {
            throw new DocumentModelResponseException(
                "The Sujiko puzzle extraction model response included given cells, but they were not an array.");
        }

        var cells = new List<SujikoCellValue>();
        foreach (var cellProperty in cellsProperty.EnumerateArray())
        {
            cells.Add(new SujikoCellValue(
                GetRequiredInt(cellProperty, "row", "Sujiko puzzle extraction"),
                GetRequiredInt(cellProperty, "column", "Sujiko puzzle extraction"),
                GetRequiredInt(cellProperty, "value", "Sujiko puzzle extraction")));
        }

        return cells;
    }

    private static IReadOnlyList<ExpenseReportLine> GetExpenseReportLines(JsonElement root)
    {
        if (!root.TryGetProperty("lines", out var linesProperty)
            || linesProperty.ValueKind != JsonValueKind.Array)
        {
            throw new DocumentModelResponseException(
                "The expense report extraction model response did not include a 'lines' array.");
        }

        var lines = new List<ExpenseReportLine>();
        foreach (var lineProperty in linesProperty.EnumerateArray())
        {
            var description = GetOptionalString(lineProperty, "description")
                ?? GetOptionalString(lineProperty, "item");
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            lines.Add(new ExpenseReportLine(
                GetOptionalDate(lineProperty, "date"),
                description,
                GetOptionalString(lineProperty, "category"),
                GetRequiredDecimal(lineProperty, "amount", "expense report extraction"),
                GetOptionalString(lineProperty, "receiptReference")
                    ?? GetOptionalString(lineProperty, "receipt")));
        }

        return lines;
    }

    private static bool? GetOptionalBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        if (property.ValueKind == JsonValueKind.String
            && bool.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
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

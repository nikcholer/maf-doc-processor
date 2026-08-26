using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Tests;

public sealed class ModelResponseParsersTests
{
    [Fact]
    public void ParseClassification_ParsesSupportedCategoryAndConfidence()
    {
        var result = ModelResponseParsers.ParseClassification("""
            {
              "category": "Receipt",
              "confidence": "0.92",
              "documentTypeDescription": "supermarket receipt",
              "confidenceReasoning": "Looks like a till receipt."
            }
            """);

        Assert.Equal(DocumentCategory.Receipt, result.Category);
        Assert.Equal(0.92m, result.Confidence);
        Assert.Equal("supermarket receipt", result.DocumentTypeDescription);
        Assert.Equal("Looks like a till receipt.", result.ConfidenceReasoning);
    }

    [Fact]
    public void ParseClassification_ParsesShoppingListCategory()
    {
        var result = ModelResponseParsers.ParseClassification("""{"category":"ShoppingList","confidence":0.8,"documentTypeDescription":"handwritten shopping list"}""");

        Assert.Equal(DocumentCategory.ShoppingList, result.Category);
        Assert.Equal("handwritten shopping list", result.DocumentTypeDescription);
    }

    [Fact]
    public void ParseClassification_ParsesPlainTextGroceryListDescription()
    {
        var result = ModelResponseParsers.ParseClassification("Grocery list");

        Assert.Equal(DocumentCategory.ShoppingList, result.Category);
        Assert.Equal("Grocery list", result.DocumentTypeDescription);
        Assert.Null(result.Confidence);
    }

    [Fact]
    public void ParseClassification_ParsesPlainTextSujikoDescription()
    {
        var result = ModelResponseParsers.ParseClassification("Sujiko puzzle");

        Assert.Equal(DocumentCategory.SujikoPuzzle, result.Category);
        Assert.Equal("Sujiko puzzle", result.DocumentTypeDescription);
        Assert.Null(result.Confidence);
    }

    [Fact]
    public void ParseClassification_RejectsInvalidJson()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseClassification("not json"));

        Assert.Contains("invalid JSON", exception.Message);
    }

    [Fact]
    public void ParseClassification_ParsesFencedJson()
    {
        var result = ModelResponseParsers.ParseClassification("""
            ```json
            {
              "category": "Receipt",
              "confidence": 0.81,
              "reasoning": "The image contains a till receipt."
            }
            ```
            """);

        Assert.Equal(DocumentCategory.Receipt, result.Category);
        Assert.Equal(0.81m, result.Confidence);
    }

    [Fact]
    public void ParseReceipt_NormalizesCurrencyAndDate()
    {
        var result = ModelResponseParsers.ParseReceipt("""
            {
              "storeName": "Meadow Vale Supermarket",
              "totalAmount": "21.02",
              "purchaseDate": "2024-05-28",
              "paymentMethod": "Visa",
              "currencyCode": "gbp"
            }
            """);

        Assert.Equal("Meadow Vale Supermarket", result.StoreName);
        Assert.Equal(21.02m, result.TotalAmount);
        Assert.Equal(new DateOnly(2024, 5, 28), result.PurchaseDate);
        Assert.Equal("GBP", result.CurrencyCode);
    }

    [Fact]
    public void ParseReceipt_ParsesJsonSurroundedByText()
    {
        var result = ModelResponseParsers.ParseReceipt("""
            Here is the extracted receipt JSON:
            {
              "storeName": "Meadow Vale Supermarket",
              "totalAmount": 21.02,
              "purchaseDate": "2024-05-28",
              "paymentMethod": "Visa Contactless",
              "currencyCode": "GBP"
            }
            """);

        Assert.Equal("Meadow Vale Supermarket", result.StoreName);
        Assert.Equal(21.02m, result.TotalAmount);
    }

    [Fact]
    public void ParseReceipt_RejectsMissingRequiredTotal()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseReceipt("""
                {
                  "storeName": "Meadow Vale Supermarket",
                  "purchaseDate": "2024-05-28",
                  "paymentMethod": "Visa",
                  "currencyCode": "GBP"
                }
                """));

        Assert.Contains("totalAmount", exception.Message);
    }

    [Fact]
    public void ParseShoppingList_ParsesItems()
    {
        var result = ModelResponseParsers.ParseShoppingList("""
            {
              "title": "Weekly groceries",
              "items": [
                { "name": "milk", "quantity": 2, "unit": "pints", "isChecked": false },
                { "item": "bread", "checked": true }
              ],
              "notes": "written in pencil"
            }
            """);

        Assert.Equal("Weekly groceries", result.Title);
        Assert.Equal("milk", result.Items[0].Name);
        Assert.Equal(2m, result.Items[0].Quantity);
        Assert.False(result.Items[0].IsChecked);
        Assert.Equal("bread", result.Items[1].Name);
        Assert.True(result.Items[1].IsChecked);
        Assert.Equal("written in pencil", result.Notes);
    }

    [Fact]
    public void ParseShoppingList_RejectsMissingItems()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseShoppingList("""{"title":"Weekly groceries"}"""));

        Assert.Contains("items", exception.Message);
    }

    [Fact]
    public void ParseClassification_ParsesExpenseReportCategory()
    {
        var result = ModelResponseParsers.ParseClassification("""
            {"category":"ExpenseReport","confidence":0.97,"documentTypeDescription":"employee expense report"}
            """);

        Assert.Equal(DocumentCategory.ExpenseReport, result.Category);
        Assert.Equal("employee expense report", result.DocumentTypeDescription);
    }

    [Fact]
    public void ParseClassification_ParsesPlainTextExpenseClaimDescription()
    {
        var result = ModelResponseParsers.ParseClassification("Expense claim form");

        Assert.Equal(DocumentCategory.ExpenseReport, result.Category);
        Assert.Equal("Expense claim form", result.DocumentTypeDescription);
        Assert.Null(result.Confidence);
    }

    [Fact]
    public void ParseExpenseReport_ParsesVisibleFieldsAndNormalizesCurrency()
    {
        var result = ModelResponseParsers.ParseExpenseReport("""
            {
              "reportNumber": "ER-2026-014",
              "title": "EXPENSE REPORT",
              "employeeName": "Alex Example",
              "periodStart": "2026-08-01",
              "periodEnd": "2026-08-20",
              "currencyCode": "gbp",
              "claimedTotal": "48.50",
              "notes": "Synthetic valid example.",
              "visibleApprovalStatus": "Not yet submitted",
              "lines": [
                {
                  "date": "2026-08-04",
                  "description": "Train fare",
                  "amount": 18.50,
                  "receiptReference": "R-001"
                },
                {
                  "date": "2026-08-12",
                  "item": "Client lunch",
                  "amount": "30.00",
                  "receipt": "R-002"
                }
              ]
            }
            """);

        Assert.Equal("ER-2026-014", result.ReportNumber);
        Assert.Equal("Alex Example", result.ClaimantName);
        Assert.Equal(new DateOnly(2026, 8, 1), result.PeriodStart);
        Assert.Equal("GBP", result.CurrencyCode);
        Assert.Equal(48.50m, result.ClaimedTotal);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("Train fare", result.Lines[0].Description);
        Assert.Equal("R-001", result.Lines[0].ReceiptReference);
        Assert.Equal("Client lunch", result.Lines[1].Description);
        Assert.Equal("R-002", result.Lines[1].ReceiptReference);
    }

    [Fact]
    public void ParseExpenseReport_RejectsMissingClaimedTotal()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseExpenseReport("""
                {
                  "reportNumber": "ER-2026-014",
                  "currencyCode": "GBP",
                  "lines": [
                    { "description": "Train fare", "amount": 18.50 }
                  ]
                }
                """));

        Assert.Contains("claimedTotal", exception.Message);
    }

    [Fact]
    public void ParseExpenseReport_RejectsMissingLinesArray()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseExpenseReport("""
                {
                  "reportNumber": "ER-2026-014",
                  "currencyCode": "GBP",
                  "claimedTotal": 48.50
                }
                """));

        Assert.Contains("lines", exception.Message);
    }

    [Fact]
    public void ParseSujikoPuzzle_ParsesTotalsAndGivenCells()
    {
        var result = ModelResponseParsers.ParseSujikoPuzzle("""
            {
              "quadrantTotals": {
                "topLeft": 21,
                "topRight": "12",
                "bottomLeft": 21,
                "bottomRight": 17
              },
              "givenCells": [
                { "row": 2, "column": 2, "value": 1 },
                { "row": 3, "column": 2, "value": 8 }
              ]
            }
            """);

        Assert.Equal(21, result.QuadrantTotals.TopLeft);
        Assert.Equal(12, result.QuadrantTotals.TopRight);
        Assert.Equal(21, result.QuadrantTotals.BottomLeft);
        Assert.Equal(17, result.QuadrantTotals.BottomRight);
        Assert.Equal(new SujikoCellValue(2, 2, 1), result.GivenCells[0]);
        Assert.Equal(new SujikoCellValue(3, 2, 8), result.GivenCells[1]);
    }

    [Fact]
    public void ParseSujikoPuzzle_AllowsMissingGivenCells()
    {
        var result = ModelResponseParsers.ParseSujikoPuzzle("""
            {
              "quadrantTotals": {
                "topLeft": 21,
                "topRight": 12,
                "bottomLeft": 21,
                "bottomRight": 17
              }
            }
            """);

        Assert.Empty(result.GivenCells);
    }

    [Fact]
    public void ParseSujikoPuzzle_RejectsMissingQuadrantTotals()
    {
        var exception = Assert.Throws<DocumentModelResponseException>(
            () => ModelResponseParsers.ParseSujikoPuzzle("""{"givenCells":[]}"""));

        Assert.Contains("quadrantTotals", exception.Message);
    }
}

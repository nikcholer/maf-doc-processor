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
}

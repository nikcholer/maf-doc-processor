using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Domain;
using Microsoft.Extensions.Configuration;

namespace MafDocumentProcessor.Tests;

public sealed class ApiDemoTests
{
    [Fact]
    public void LoadAiModelSettings_UsesTogetherDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = ApiConfigurationLoader.LoadAiModelSettings(configuration);

        Assert.Equal("TogetherAI", settings.DocumentClassification.Provider);
        Assert.Equal("https://api.together.ai/v1", settings.DocumentClassification.Endpoint);
        Assert.Equal("Qwen/Qwen3.5-9B", settings.DocumentClassification.ModelId);
        Assert.Equal("TOGETHER_API_KEY", settings.DocumentClassification.ApiKeyEnvironmentVariable);
        Assert.Equal(0.10m, settings.DocumentClassification.InputTokenPricePerMillionUsd);
        Assert.Equal(0.15m, settings.DocumentClassification.OutputTokenPricePerMillionUsd);
        Assert.Equal("Qwen/Qwen3.5-9B", settings.DocumentExtraction.ModelId);
        Assert.Equal(0.10m, settings.DocumentExtraction.InputTokenPricePerMillionUsd);
        Assert.Equal(0.15m, settings.DocumentExtraction.OutputTokenPricePerMillionUsd);
        Assert.Equal("google/gemma-4-31B-it", settings.TextTesting.ModelId);
    }

    [Fact]
    public void LoadAiModelSettings_UsesLegacyImageRecognitionForClassificationAndExtraction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiModels:ImageRecognition:ModelId"] = "legacy/image-model",
                ["AiModels:ImageRecognition:ServiceId"] = "legacy-image",
                ["AiModels:ImageRecognition:InputTokenPricePerMillionUsd"] = "1.23",
                ["AiModels:ImageRecognition:OutputTokenPricePerMillionUsd"] = "4.56"
            })
            .Build();

        var settings = ApiConfigurationLoader.LoadAiModelSettings(configuration);

        Assert.Equal("legacy/image-model", settings.DocumentClassification.ModelId);
        Assert.Equal("legacy/image-model", settings.DocumentExtraction.ModelId);
        Assert.Equal("legacy-image", settings.DocumentClassification.ServiceId);
        Assert.Equal("legacy-image", settings.DocumentExtraction.ServiceId);
        Assert.Equal(1.23m, settings.DocumentClassification.InputTokenPricePerMillionUsd);
        Assert.Equal(4.56m, settings.DocumentExtraction.OutputTokenPricePerMillionUsd);
    }

    [Fact]
    public void LoadModelImagePreprocessingSettings_UsesConfiguredValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ModelImagePreprocessing:Enabled"] = "true",
                ["ModelImagePreprocessing:ClassificationMaxLongEdgePixels"] = "960",
                ["ModelImagePreprocessing:ExtractionMaxLongEdgePixels"] = "1800",
                ["ModelImagePreprocessing:JpegQuality"] = "82"
            })
            .Build();

        var settings = ApiConfigurationLoader.LoadModelImagePreprocessingSettings(configuration);

        Assert.True(settings.Enabled);
        Assert.Equal(960, settings.ClassificationMaxLongEdgePixels);
        Assert.Equal(1800, settings.ExtractionMaxLongEdgePixels);
        Assert.Equal(82, settings.JpegQuality);
    }

    [Fact]
    public void DocumentImageValidator_AcceptsConfiguredPngUpload()
    {
        var validator = new DocumentImageValidator();
        var settings = new DocumentIntakeSettings();

        var result = validator.Validate(
            new DocumentImageValidationRequest("receipt.png", "image/png", 128),
            settings);

        Assert.Null(result);
    }

    [Fact]
    public void DocumentImageValidator_RejectsUnsupportedExtension()
    {
        var validator = new DocumentImageValidator();
        var settings = new DocumentIntakeSettings();

        var result = validator.Validate(
            new DocumentImageValidationRequest("receipt.gif", "image/png", 128),
            settings);

        Assert.NotNull(result);
        Assert.Equal("image", result.Field);
        Assert.Contains("Unsupported file extension", result.Message);
    }

    [Fact]
    public void DocumentProcessingResponseMapper_MapsReceiptWorkflowResultForDemoUi()
    {
        var result = CreateWorkflowResult();

        var response = DocumentProcessingResponseMapper.Map(result);

        Assert.Equal(DocumentCategory.Receipt, response.Category);
        var receipt = Assert.IsType<ReceiptData>(response.Document?.Data);
        Assert.Equal("Corner Shop", receipt.StoreName);
        Assert.Equal(PolicyDecision.Approved, response.Document?.PolicyResult?.Decision);
        Assert.True(response.Document?.Validation.IsValid);
        Assert.Equal(12, response.ModelUsage.TotalTokens);
        Assert.Equal(0.0000048m, response.ModelUsage.EstimatedTotalCostUsd);
    }

    [Fact]
    public void DocumentProcessingResponseMapper_MapsShoppingListWorkflowResultForDemoUi()
    {
        var metadata = CreateMetadata("shopping-list.png");
        var result = new DocumentProcessingResult(
            DocumentCategory.ShoppingList,
            metadata,
            new DocumentClassification(
                DocumentCategory.ShoppingList,
                0.9m,
                "shopping list layout"),
            DocumentModelUsage.FromCalls([
                new ModelTokenUsage("classification", "model", 2, 4, 6),
                new ModelTokenUsage("shopping_list_extraction", "model", 2, 4, 6)
            ]),
            Receipt: null,
            new ShoppingListData(
                "Weekly groceries",
                [new ShoppingListItem("milk", 2, "pints", false)],
                Notes: null),
            PolicyResult: null,
            ValidationResult.Valid,
            IsSuccess: true,
            Errors: [],
            Warnings: []);

        var response = DocumentProcessingResponseMapper.Map(result);

        var shoppingList = Assert.IsType<ShoppingListData>(response.Document?.Data);
        Assert.Equal(DocumentCategory.ShoppingList, response.Category);
        Assert.Equal("Weekly groceries", shoppingList.Title);
        Assert.Equal("milk", shoppingList.Items[0].Name);
        Assert.Null(response.Document?.PolicyResult);
    }

    private static DocumentProcessingResult CreateWorkflowResult()
    {
        var metadata = CreateMetadata("receipt.png");
        var receipt = new ReceiptData(
            "Corner Shop",
            10.5m,
            new DateOnly(2026, 5, 11),
            "Visa",
            "GBP");
        var policy = new ReceiptPolicyResult(
            IsWithinReviewThreshold: true,
            HasPaymentMethod: true,
            PolicyDecision.Approved,
            ["Receipt is within review threshold."]);

        return new DocumentProcessingResult(
            DocumentCategory.Receipt,
            metadata,
            new DocumentClassification(
                DocumentCategory.Receipt,
                0.9m,
                "receipt layout"),
            DocumentModelUsage.FromCalls([
                new ModelTokenUsage("classification", "model", 2, 4, 6, 0.20m, 0.50m, 0.0000004m, 0.000002m, 0.0000024m),
                new ModelTokenUsage("receipt_extraction", "model", 2, 4, 6, 0.20m, 0.50m, 0.0000004m, 0.000002m, 0.0000024m)
            ]),
            receipt,
            ShoppingList: null,
            policy,
            ValidationResult.Valid,
            IsSuccess: true,
            Errors: [],
            Warnings: []);
    }

    private static DocumentMetadata CreateMetadata(string fileName)
    {
        return new DocumentMetadata(
            fileName,
            "image/png",
            FileSizeBytes: 128,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            SourceId: "api-test",
            ModelId: "google/gemma-4-31B-it",
            ClassificationConfidence: 0.9m);
    }
}

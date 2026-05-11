using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Domain;
using Microsoft.Extensions.Configuration;

namespace MafDocumentProcessor.Tests;

public sealed class ApiDemoTests
{
    [Fact]
    public void LoadAiModelSettings_UsesTogetherGemma4Defaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = ApiConfigurationLoader.LoadAiModelSettings(configuration);

        Assert.Equal("TogetherAI", settings.ImageRecognition.Provider);
        Assert.Equal("https://api.together.ai/v1", settings.ImageRecognition.Endpoint);
        Assert.Equal("google/gemma-4-31B-it", settings.ImageRecognition.ModelId);
        Assert.Equal("TOGETHER_API_KEY", settings.ImageRecognition.ApiKeyEnvironmentVariable);
        Assert.Equal(0.20m, settings.ImageRecognition.InputTokenPricePerMillionUsd);
        Assert.Equal(0.50m, settings.ImageRecognition.OutputTokenPricePerMillionUsd);
        Assert.Equal("google/gemma-4-31B-it", settings.TextTesting.ModelId);
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
        Assert.Equal("Corner Shop", response.Document?.Data.StoreName);
        Assert.Equal(PolicyDecision.Approved, response.Document?.PolicyResult?.Decision);
        Assert.True(response.Document?.Validation.IsValid);
        Assert.Equal(12, response.ModelUsage.TotalTokens);
        Assert.Equal(0.0000048m, response.ModelUsage.EstimatedTotalCostUsd);
    }

    private static ReceiptProcessingResult CreateWorkflowResult()
    {
        var metadata = new DocumentMetadata(
            "receipt.png",
            "image/png",
            FileSizeBytes: 128,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            SourceId: "api-test",
            ModelId: "google/gemma-4-31B-it",
            ClassificationConfidence: 0.9m);
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

        return new ReceiptProcessingResult(
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
            policy,
            ValidationResult.Valid,
            IsSuccess: true,
            Errors: [],
            Warnings: []);
    }
}

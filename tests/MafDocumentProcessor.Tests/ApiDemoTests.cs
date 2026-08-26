using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Workflow;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        Assert.Equal(60, settings.DocumentClassification.RequestTimeoutSeconds);
        Assert.Equal(2, settings.DocumentClassification.MaxRetryAttempts);
        Assert.Equal(500, settings.DocumentClassification.RetryBaseDelayMilliseconds);
        Assert.Equal(0.10m, settings.DocumentClassification.InputTokenPricePerMillionUsd);
        Assert.Equal(0.15m, settings.DocumentClassification.OutputTokenPricePerMillionUsd);
        Assert.Equal("Qwen/Qwen3.5-9B", settings.DocumentExtraction.ModelId);
        Assert.Equal(60, settings.DocumentExtraction.RequestTimeoutSeconds);
        Assert.Equal(2, settings.DocumentExtraction.MaxRetryAttempts);
        Assert.Equal(500, settings.DocumentExtraction.RetryBaseDelayMilliseconds);
        Assert.Equal(0.10m, settings.DocumentExtraction.InputTokenPricePerMillionUsd);
        Assert.Equal(0.15m, settings.DocumentExtraction.OutputTokenPricePerMillionUsd);
        Assert.Equal("Qwen/Qwen3.5-9B", settings.DocumentRegionDetection.ModelId);
        Assert.Equal("document-region-detection", settings.DocumentRegionDetection.ServiceId);
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
    public void LoadAiModelSettings_UsesConfiguredRetryPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiModels:DocumentClassification:MaxRetryAttempts"] = "3",
                ["AiModels:DocumentClassification:RetryBaseDelayMilliseconds"] = "250"
            })
            .Build();

        var settings = ApiConfigurationLoader.LoadAiModelSettings(configuration);

        Assert.Equal(3, settings.DocumentClassification.MaxRetryAttempts);
        Assert.Equal(250, settings.DocumentClassification.RetryBaseDelayMilliseconds);
        Assert.Equal(2, settings.DocumentExtraction.MaxRetryAttempts);
        Assert.Equal(500, settings.DocumentExtraction.RetryBaseDelayMilliseconds);
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
                ["ModelImagePreprocessing:JpegQuality"] = "82",
                ["ModelImagePreprocessing:RegionDetectionMaxLongEdgePixels"] = "1400"
            })
            .Build();

        var settings = ApiConfigurationLoader.LoadModelImagePreprocessingSettings(configuration);

        Assert.True(settings.Enabled);
        Assert.Equal(960, settings.ClassificationMaxLongEdgePixels);
        Assert.Equal(1800, settings.ExtractionMaxLongEdgePixels);
        Assert.Equal(82, settings.JpegQuality);
        Assert.Equal(1400, settings.RegionDetectionMaxLongEdgePixels);
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
        Assert.Equal(HumanReviewStatus.NotRequired, response.HumanReview.Status);
        Assert.True(response.Document?.Validation.IsValid);
        Assert.Equal(12, response.ModelUsage.TotalTokens);
        Assert.Equal(0.0000048m, response.ModelUsage.EstimatedTotalCostUsd);
        Assert.Equal(1200, response.ModelUsage.TotalDurationMilliseconds);
    }

    [Fact]
    public void DocumentProcessingResponseMapper_MapsHumanReviewForDemoUi()
    {
        var result = CreateWorkflowResult() with
        {
            HumanReview = new HumanReviewResult(
                HumanReviewStatus.Required,
                ["Receipt payment method is missing."],
                RequiresUserAttestation: false,
                AttestationPrompt: null)
        };

        var response = DocumentProcessingResponseMapper.Map(result);

        Assert.Equal(HumanReviewStatus.Required, response.HumanReview.Status);
        Assert.True(response.HumanReview.IsRequired);
        Assert.Contains("payment method is missing", response.HumanReview.Reasons[0]);
    }

    [Fact]
    public void DocumentProcessingResponseMapper_SerializesModelLatencyForDemoUi()
    {
        var response = DocumentProcessingResponseMapper.Map(CreateWorkflowResult());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(response, options);

        Assert.Contains("\"totalDurationMilliseconds\":1200", json);
        Assert.Contains("\"durationMilliseconds\":400", json);
        Assert.Contains("\"durationMilliseconds\":800", json);
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
            SujikoPuzzle: null,
            ExpenseReport: null,
            PolicyResult: null,
            ExpensePolicy: null,
            ValidationResult.Valid,
            HumanReviewResult.NotRequired,
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

    [Fact]
    public void DocumentProcessingResponseMapper_MapsSujikoWorkflowResultForDemoUi()
    {
        var metadata = CreateMetadata("sujiko.png");
        var result = new DocumentProcessingResult(
            DocumentCategory.SujikoPuzzle,
            metadata,
            new DocumentClassification(
                DocumentCategory.SujikoPuzzle,
                0.93m,
                "Sujiko grid layout"),
            DocumentModelUsage.FromCalls([
                new ModelTokenUsage("classification", "model", 2, 4, 6),
                new ModelTokenUsage("sujiko_puzzle_extraction", "model", 2, 4, 6)
            ]),
            Receipt: null,
            ShoppingList: null,
            new SujikoPuzzleData(
                new SujikoQuadrantTotals(21, 12, 21, 17),
                [
                    new SujikoCellValue(2, 2, 1),
                    new SujikoCellValue(3, 2, 8)
                ]),
            ExpenseReport: null,
            PolicyResult: null,
            ExpensePolicy: null,
            ValidationResult.Valid,
            HumanReviewResult.NotRequired,
            IsSuccess: true,
            Errors: [],
            Warnings: []);

        var response = DocumentProcessingResponseMapper.Map(result);

        var puzzle = Assert.IsType<SujikoPuzzleData>(response.Document?.Data);
        Assert.Equal(DocumentCategory.SujikoPuzzle, response.Category);
        Assert.Equal(21, puzzle.QuadrantTotals.TopLeft);
        Assert.Equal(17, puzzle.QuadrantTotals.BottomRight);
        Assert.Equal(new SujikoCellValue(3, 2, 8), puzzle.GivenCells[1]);
        Assert.Null(response.Document?.PolicyResult);
    }

    [Fact]
    public void DocumentProcessingResponseMapper_MapsExpenseReportWorkflowResultForDemoUi()
    {
        var metadata = CreateMetadata("expense-report.png");
        var result = new DocumentProcessingResult(
            DocumentCategory.ExpenseReport,
            metadata,
            new DocumentClassification(
                DocumentCategory.ExpenseReport,
                0.97m,
                "expense report layout"),
            DocumentModelUsage.FromCalls([
                new ModelTokenUsage("classification", "model", 2, 4, 6),
                new ModelTokenUsage("expense_report_extraction", "model", 2, 4, 6)
            ]),
            Receipt: null,
            ShoppingList: null,
            SujikoPuzzle: null,
            new ExpenseReportData(
                "ER-2026-014",
                "EXPENSE REPORT",
                "Alex Example",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 20),
                "GBP",
                48.50m,
                [
                    new ExpenseReportLine(new DateOnly(2026, 8, 4), "Train fare", null, 18.50m, "R-001"),
                    new ExpenseReportLine(new DateOnly(2026, 8, 12), "Client lunch", null, 30.00m, "R-002")
                ],
                "Synthetic valid example.",
                "Not yet submitted"),
            PolicyResult: null,
            new ExpensePolicyResult(
                IsWithinHighValueThreshold: true,
                AllLinesHaveReceiptReferences: true,
                PolicyDecision.Approved,
                []),
            ValidationResult.Valid,
            new HumanReviewResult(
                HumanReviewStatus.Required,
                [ExpenseReportResultExecutor.AttestationPrompt],
                RequiresUserAttestation: true,
                ExpenseReportResultExecutor.AttestationPrompt),
            IsSuccess: true,
            Errors: [],
            Warnings: []);

        var response = DocumentProcessingResponseMapper.Map(result);

        var expenseReport = Assert.IsType<ExpenseReportData>(response.Document?.Data);
        Assert.Equal(DocumentCategory.ExpenseReport, response.Category);
        Assert.Equal("ER-2026-014", expenseReport.ReportNumber);
        Assert.Equal(48.50m, expenseReport.ClaimedTotal);
        Assert.Null(response.Document?.PolicyResult);
        Assert.Equal(PolicyDecision.Approved, response.Document?.ExpensePolicy?.Decision);
        Assert.Equal(HumanReviewStatus.Required, response.HumanReview.Status);
        Assert.True(response.HumanReview.RequiresUserAttestation);
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
                new ModelTokenUsage("classification", "model", 2, 4, 6, 0.20m, 0.50m, 0.0000004m, 0.000002m, 0.0000024m, 400),
                new ModelTokenUsage("receipt_extraction", "model", 2, 4, 6, 0.20m, 0.50m, 0.0000004m, 0.000002m, 0.0000024m, 800)
            ]),
            receipt,
            ShoppingList: null,
            SujikoPuzzle: null,
            ExpenseReport: null,
            policy,
            ExpensePolicy: null,
            ValidationResult.Valid,
            HumanReviewResult.NotRequired,
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

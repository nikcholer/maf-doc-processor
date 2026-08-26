using System.Diagnostics;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Xunit.Abstractions;

namespace MafDocumentProcessor.Tests;

public sealed class ExpenseReportAssetTests(ITestOutputHelper output)
{
    private const string RunLiveAssetTestsEnvironmentVariable = "MAF_RUN_LIVE_ASSET_TESTS";
    private static readonly string ValidExpenseAssetPath = Path.Combine(
        AppContext.BaseDirectory,
        "next-scenario-samples",
        "sources",
        "expense-valid.png");

    [Fact]
    public void ValidExpenseReportAsset_IsAvailableForRegressionTesting()
    {
        Assert.True(File.Exists(ValidExpenseAssetPath), ValidExpenseAssetPath);
        var content = File.ReadAllBytes(ValidExpenseAssetPath);
        Assert.True(content.Length > 1_000);
    }

    [Fact]
    public async Task RunAsync_CanBeLiveCheckedAgainstValidExpenseReportAsset()
    {
        if (Environment.GetEnvironmentVariable(RunLiveAssetTestsEnvironmentVariable) != "1")
        {
            return;
        }

        var settings = AiModelSettingsDefaults.CreateTogetherDefaults();
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                settings.DocumentClassification.ApiKeyEnvironmentVariable)))
        {
            throw new InvalidOperationException(
                $"Set {settings.DocumentClassification.ApiKeyEnvironmentVariable} to run live asset tests.");
        }

        var request = new FileRequest(
            File.ReadAllBytes(ValidExpenseAssetPath),
            "expense-valid.png",
            "image/png",
            new FileInfo(ValidExpenseAssetPath).Length,
            DateTimeOffset.UtcNow,
            SourceId: "live-expense-valid");
        var chatClient = new OpenAICompatibleModelChatClient();
        var workflow = new DocumentProcessingWorkflow(
            new ModelDocumentClassifier(chatClient, settings.DocumentClassification),
            new ModelReceiptExtractor(chatClient, settings.DocumentExtraction),
            new ModelShoppingListExtractor(chatClient, settings.DocumentExtraction),
            new ReceiptPolicyOptions(),
            sujikoPuzzleExtractor: new ModelSujikoPuzzleExtractor(
                chatClient,
                settings.DocumentExtraction),
            expenseReportExtractor: new ModelExpenseReportExtractor(
                chatClient,
                settings.DocumentExtraction),
            expensePolicyOptions: new ExpensePolicyOptions());

        var stopwatch = Stopwatch.StartNew();
        var result = await workflow.RunAsync(request, CancellationToken.None);
        stopwatch.Stop();

        output.WriteLine(
            "Live expense report: category={0} success={1} valid={2} review={3} lines={4} claimed={5} durationMs={6} tokens={7} cost={8}",
            result.Category,
            result.IsSuccess,
            result.Validation.IsValid,
            result.HumanReview.Status,
            result.ExpenseReport?.Lines.Count,
            result.ExpenseReport?.ClaimedTotal,
            stopwatch.ElapsedMilliseconds,
            result.ModelUsage.TotalTokens,
            result.ModelUsage.EstimatedTotalCostUsd);

        Assert.Equal(DocumentCategory.ExpenseReport, result.Category);
        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.NotNull(result.ExpenseReport);
        Assert.Equal("GBP", result.ExpenseReport.CurrencyCode);
        Assert.Equal(48.50m, result.ExpenseReport.ClaimedTotal);
        Assert.Equal(2, result.ExpenseReport.Lines.Count);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.True(result.HumanReview.RequiresUserAttestation);
        Assert.Contains(
            ExpenseReportResultExecutor.AttestationPrompt,
            result.HumanReview.Reasons);
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "classification");
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "expense_report_extraction");
    }
}

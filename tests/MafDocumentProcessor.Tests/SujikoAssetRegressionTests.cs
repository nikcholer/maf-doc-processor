using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using SixLabors.ImageSharp;
using Xunit.Abstractions;

namespace MafDocumentProcessor.Tests;

public sealed class SujikoAssetRegressionTests(ITestOutputHelper output)
{
    private const string SyntheticSujikoAssetName = "synthetic-sujiko-newspaper.jpg";
    private const string SyntheticSujikoAssetSha256 =
        "4429A427DF01DE20BE381A4B58DB5307B4B8663C2951F9B68B7A1DBC3D6AB633";
    private const string RunLiveAssetTestsEnvironmentVariable = "MAF_RUN_LIVE_ASSET_TESTS";

    [Fact]
    public void SyntheticSujikoAsset_IsPublicSafeAndAvailableForRegressionTesting()
    {
        var asset = LoadSyntheticSujikoAsset();

        Assert.Equal("image/jpeg", asset.ContentType);
        Assert.True(asset.Content.Length > 20_000);
        Assert.Equal(SyntheticSujikoAssetSha256, Convert.ToHexString(SHA256.HashData(asset.Content)));
        using var image = Image.Load(asset.Content);
        Assert.Equal(448, image.Width);
        Assert.Equal(506, image.Height);
        Assert.Null(image.Metadata.ExifProfile);

        var expected = ExpectedSyntheticSujikoPuzzle();
        Assert.Equal(20, expected.QuadrantTotals.TopLeft);
        Assert.Equal(11, expected.QuadrantTotals.TopRight);
        Assert.Equal(24, expected.QuadrantTotals.BottomLeft);
        Assert.Equal(23, expected.QuadrantTotals.BottomRight);
        Assert.Equal(
            [
                new SujikoCellValue(Row: 1, Column: 3, Value: 3),
                new SujikoCellValue(Row: 3, Column: 2, Value: 7)
            ],
            expected.GivenCells);
    }

    [Fact]
    public async Task RunAsync_CanBeLiveCheckedAndMeasuredAgainstSyntheticSujikoAsset()
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

        var asset = LoadSyntheticSujikoAsset();
        var chatClient = new OpenAICompatibleModelChatClient();
        var workflow = new DocumentProcessingWorkflow(
            new ModelDocumentClassifier(chatClient, settings.DocumentClassification),
            new ModelReceiptExtractor(chatClient, settings.DocumentExtraction),
            new ModelShoppingListExtractor(chatClient, settings.DocumentExtraction),
            new ReceiptPolicyOptions(),
            sujikoPuzzleExtractor: new ModelSujikoPuzzleExtractor(
                chatClient,
                settings.DocumentExtraction));

        var stopwatch = Stopwatch.StartNew();
        var result = await workflow.RunAsync(asset, CancellationToken.None);
        stopwatch.Stop();

        var expectedPuzzle = ExpectedSyntheticSujikoPuzzle();
        var matchesExpectedPuzzle = result.SujikoPuzzle is not null
            && SujikoPuzzlesMatch(expectedPuzzle, result.SujikoPuzzle);
        WriteMeasurement(result, stopwatch.ElapsedMilliseconds, matchesExpectedPuzzle);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.SujikoPuzzle, result.Category);
        Assert.True(result.Validation.IsValid);
        Assert.NotNull(result.SujikoPuzzle);
        AssertSujikoPuzzle(expectedPuzzle, result.SujikoPuzzle);
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "classification");
        Assert.Contains(result.ModelUsage.Calls, call => call.Operation == "sujiko_puzzle_extraction");
        Assert.All(result.ModelUsage.Calls, call =>
        {
            Assert.NotNull(call.TotalTokens);
            Assert.NotNull(call.EstimatedTotalCostUsd);
            Assert.NotNull(call.DurationMilliseconds);
        });
    }

    private void WriteMeasurement(
        DocumentProcessingResult result,
        long workflowElapsedMilliseconds,
        bool matchesExpectedPuzzle)
    {
        var measurement = new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            SourceAsset = SyntheticSujikoAssetName,
            WorkflowElapsedMilliseconds = workflowElapsedMilliseconds,
            Category = result.Category.ToString(),
            result.IsSuccess,
            IsValid = result.Validation.IsValid,
            HumanReviewStatus = result.HumanReview.Status.ToString(),
            MatchesExpectedPuzzle = matchesExpectedPuzzle,
            ModelUsage = result.ModelUsage
        };

        output.WriteLine(JsonSerializer.Serialize(
            measurement,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static FileRequest LoadSyntheticSujikoAsset()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            SyntheticSujikoAssetName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Could not find Sujiko regression asset at {path}.",
                path);
        }

        var bytes = File.ReadAllBytes(path);
        return new FileRequest(
            bytes,
            SyntheticSujikoAssetName,
            "image/jpeg",
            bytes.Length,
            DateTimeOffset.Parse("2026-08-27T11:46:31Z"),
            SourceId: "sujiko-synthetic-regression");
    }

    private static SujikoPuzzleData ExpectedSyntheticSujikoPuzzle()
    {
        return new SujikoPuzzleData(
            new SujikoQuadrantTotals(
                TopLeft: 20,
                TopRight: 11,
                BottomLeft: 24,
                BottomRight: 23),
            [
                new SujikoCellValue(Row: 1, Column: 3, Value: 3),
                new SujikoCellValue(Row: 3, Column: 2, Value: 7)
            ]);
    }

    private static void AssertSujikoPuzzle(
        SujikoPuzzleData expected,
        SujikoPuzzleData actual)
    {
        Assert.Equal(expected.QuadrantTotals, actual.QuadrantTotals);
        Assert.Equal(expected.GivenCells.OrderBy(cell => (cell.Row, cell.Column)).ToArray(),
            actual.GivenCells.OrderBy(cell => (cell.Row, cell.Column)).ToArray());
    }

    private static bool SujikoPuzzlesMatch(
        SujikoPuzzleData expected,
        SujikoPuzzleData actual)
    {
        return expected.QuadrantTotals == actual.QuadrantTotals
            && expected.GivenCells.OrderBy(cell => (cell.Row, cell.Column)).SequenceEqual(
                actual.GivenCells.OrderBy(cell => (cell.Row, cell.Column)));
    }
}

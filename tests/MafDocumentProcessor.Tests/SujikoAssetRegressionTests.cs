using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Tests;

public sealed class SujikoAssetRegressionTests
{
    private const string RotatedSujikoAssetName = "IMG20260513194450.jpg";
    private const string RunLiveAssetTestsEnvironmentVariable = "MAF_RUN_LIVE_ASSET_TESTS";

    [Fact]
    public void RotatedSujikoAsset_IsAvailableForRegressionTesting()
    {
        var asset = LoadRotatedSujikoAsset();

        Assert.Equal("image/jpeg", asset.ContentType);
        Assert.True(asset.Content.Length > 100_000);
        var expected = ExpectedRotatedSujikoPuzzle();
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
    public async Task ExtractSujikoPuzzleAsync_CanBeLiveCheckedAgainstRotatedAsset()
    {
        if (Environment.GetEnvironmentVariable(RunLiveAssetTestsEnvironmentVariable) != "1")
        {
            return;
        }

        var settings = AiModelSettingsDefaults.CreateTogetherDefaults().DocumentExtraction;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(settings.ApiKeyEnvironmentVariable)))
        {
            throw new InvalidOperationException(
                $"Set {settings.ApiKeyEnvironmentVariable} to run live asset tests.");
        }

        var asset = LoadRotatedSujikoAsset();
        var extractor = new ModelSujikoPuzzleExtractor(
            new OpenAICompatibleModelChatClient(),
            settings);

        var result = await extractor.ExtractSujikoPuzzleAsync(asset, CancellationToken.None);

        AssertSujikoPuzzle(ExpectedRotatedSujikoPuzzle(), result.Value);
    }

    private static FileRequest LoadRotatedSujikoAsset()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            RotatedSujikoAssetName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Could not find Sujiko regression asset at {path}.",
                path);
        }

        var bytes = File.ReadAllBytes(path);
        return new FileRequest(
            bytes,
            RotatedSujikoAssetName,
            "image/jpeg",
            bytes.Length,
            DateTimeOffset.Parse("2026-05-13T19:44:50Z"),
            SourceId: "sujiko-rotated-regression");
    }

    private static SujikoPuzzleData ExpectedRotatedSujikoPuzzle()
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
}

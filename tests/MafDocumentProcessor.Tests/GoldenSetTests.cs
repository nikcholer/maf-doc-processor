using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class GoldenSetTests
{
    private const string ManifestName = "current-document-paths.json";
    private static readonly DateTimeOffset FixedReceivedAt =
        DateTimeOffset.Parse("2026-08-25T12:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CurrentDocumentPaths_MatchVersionedGoldenResults()
    {
        var manifest = LoadManifest();

        Assert.Equal(1, manifest.Version);
        Assert.Equal(4, manifest.Cases.Count);
        Assert.Equal(
            [DocumentCategory.Receipt, DocumentCategory.ShoppingList, DocumentCategory.SujikoPuzzle, DocumentCategory.Unknown],
            manifest.Cases.Select(testCase => testCase.Expected.Category).OrderBy(category => category).ToArray());
        Assert.Equal(
            manifest.Cases.Count,
            manifest.Cases.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var testCase in manifest.Cases)
        {
            var workflow = CreateWorkflow(testCase.ModelOutputs);
            var request = CreateRequest(testCase);

            var result = await workflow.RunAsync(request, CancellationToken.None);

            AssertGoldenResult(testCase, GoldenResultSnapshot.FromResult(result));
        }
    }

    private static GoldenSetManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden-set", ManifestName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find golden-set manifest at {path}.", path);
        }

        return JsonSerializer.Deserialize<GoldenSetManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The golden-set manifest was empty.");
    }

    private static DocumentProcessingWorkflow CreateWorkflow(GoldenModelOutputs outputs)
    {
        return new DocumentProcessingWorkflow(
            new GoldenDocumentClassifier(outputs.Classification),
            new GoldenReceiptExtractor(outputs.Receipt),
            new GoldenShoppingListExtractor(outputs.ShoppingList),
            new ReceiptPolicyOptions(),
            sujikoPuzzleExtractor: new GoldenSujikoPuzzleExtractor(outputs.SujikoPuzzle));
    }

    private static FileRequest CreateRequest(GoldenSetCase testCase)
    {
        var content = testCase.Source.AssetName is not null
            ? LoadAsset(testCase.Source.AssetName)
            : RenderSyntheticDocument(testCase.Source.Lines
                ?? throw new InvalidOperationException(
                    $"Golden case '{testCase.Id}' has neither an asset nor synthetic lines."));

        return new FileRequest(
            content,
            testCase.Source.FileName,
            testCase.Source.ContentType,
            content.LongLength,
            FixedReceivedAt,
            SourceId: $"golden:{testCase.Id}");
    }

    private static byte[] LoadAsset(string assetName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", assetName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find golden-set asset at {path}.", path);
        }

        return File.ReadAllBytes(path);
    }

    private static byte[] RenderSyntheticDocument(IReadOnlyList<string> lines)
    {
        const int width = 900;
        const int height = 600;
        const int scale = 7;
        const int left = 60;
        const int top = 70;
        const int lineAdvance = 82;

        using var image = new Image<Rgba32>(width, height, Color.White);
        image.ProcessPixelRows(accessor =>
        {
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex].ToUpperInvariant();
                for (var characterIndex = 0; characterIndex < line.Length; characterIndex++)
                {
                    DrawCharacter(
                        accessor,
                        line[characterIndex],
                        left + characterIndex * 6 * scale,
                        top + lineIndex * lineAdvance,
                        scale);
                }
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void DrawCharacter(
        PixelAccessor<Rgba32> pixels,
        char character,
        int originX,
        int originY,
        int scale)
    {
        if (!BitmapFont.TryGetValue(character, out var columns))
        {
            throw new InvalidOperationException($"The synthetic-document font does not contain '{character}'.");
        }

        for (var glyphX = 0; glyphX < columns.Length; glyphX++)
        {
            for (var glyphY = 0; glyphY < 7; glyphY++)
            {
                if ((columns[glyphX] & (1 << glyphY)) == 0)
                {
                    continue;
                }

                for (var offsetY = 0; offsetY < scale; offsetY++)
                {
                    var row = pixels.GetRowSpan(originY + glyphY * scale + offsetY);
                    for (var offsetX = 0; offsetX < scale; offsetX++)
                    {
                        row[originX + glyphX * scale + offsetX] = new Rgba32(24, 24, 24);
                    }
                }
            }
        }
    }

    private static void AssertGoldenResult(GoldenSetCase testCase, GoldenResultSnapshot actual)
    {
        var expectedJson = JsonSerializer.Serialize(testCase.Expected, JsonOptions);
        var actualJson = JsonSerializer.Serialize(actual, JsonOptions);

        Assert.True(
            string.Equals(expectedJson, actualJson, StringComparison.Ordinal),
            $"Golden case '{testCase.Id}' changed.{Environment.NewLine}Expected: {expectedJson}{Environment.NewLine}Actual: {actualJson}");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static readonly IReadOnlyDictionary<char, byte[]> BitmapFont = new Dictionary<char, byte[]>
    {
        [' '] = [0x00, 0x00, 0x00, 0x00, 0x00],
        ['-'] = [0x08, 0x08, 0x08, 0x08, 0x08],
        ['.'] = [0x00, 0x60, 0x60, 0x00, 0x00],
        ['0'] = [0x3E, 0x51, 0x49, 0x45, 0x3E],
        ['1'] = [0x00, 0x42, 0x7F, 0x40, 0x00],
        ['2'] = [0x42, 0x61, 0x51, 0x49, 0x46],
        ['3'] = [0x21, 0x41, 0x45, 0x4B, 0x31],
        ['4'] = [0x18, 0x14, 0x12, 0x7F, 0x10],
        ['5'] = [0x27, 0x45, 0x45, 0x45, 0x39],
        ['6'] = [0x3C, 0x4A, 0x49, 0x49, 0x30],
        ['7'] = [0x01, 0x71, 0x09, 0x05, 0x03],
        ['8'] = [0x36, 0x49, 0x49, 0x49, 0x36],
        ['9'] = [0x06, 0x49, 0x49, 0x29, 0x1E],
        ['A'] = [0x7E, 0x11, 0x11, 0x11, 0x7E],
        ['B'] = [0x7F, 0x49, 0x49, 0x49, 0x36],
        ['C'] = [0x3E, 0x41, 0x41, 0x41, 0x22],
        ['D'] = [0x7F, 0x41, 0x41, 0x22, 0x1C],
        ['E'] = [0x7F, 0x49, 0x49, 0x49, 0x41],
        ['F'] = [0x7F, 0x09, 0x09, 0x09, 0x01],
        ['G'] = [0x3E, 0x41, 0x49, 0x49, 0x7A],
        ['H'] = [0x7F, 0x08, 0x08, 0x08, 0x7F],
        ['I'] = [0x00, 0x41, 0x7F, 0x41, 0x00],
        ['J'] = [0x20, 0x40, 0x41, 0x3F, 0x01],
        ['K'] = [0x7F, 0x08, 0x14, 0x22, 0x41],
        ['L'] = [0x7F, 0x40, 0x40, 0x40, 0x40],
        ['M'] = [0x7F, 0x02, 0x0C, 0x02, 0x7F],
        ['N'] = [0x7F, 0x04, 0x08, 0x10, 0x7F],
        ['O'] = [0x3E, 0x41, 0x41, 0x41, 0x3E],
        ['P'] = [0x7F, 0x09, 0x09, 0x09, 0x06],
        ['Q'] = [0x3E, 0x41, 0x51, 0x21, 0x5E],
        ['R'] = [0x7F, 0x09, 0x19, 0x29, 0x46],
        ['S'] = [0x46, 0x49, 0x49, 0x49, 0x31],
        ['T'] = [0x01, 0x01, 0x7F, 0x01, 0x01],
        ['U'] = [0x3F, 0x40, 0x40, 0x40, 0x3F],
        ['V'] = [0x1F, 0x20, 0x40, 0x20, 0x1F],
        ['W'] = [0x3F, 0x40, 0x38, 0x40, 0x3F],
        ['X'] = [0x63, 0x14, 0x08, 0x14, 0x63],
        ['Y'] = [0x07, 0x08, 0x70, 0x08, 0x07],
        ['Z'] = [0x61, 0x51, 0x49, 0x45, 0x43]
    };

    private sealed record GoldenSetManifest(int Version, IReadOnlyList<GoldenSetCase> Cases);

    private sealed record GoldenSetCase(
        string Id,
        GoldenSource Source,
        GoldenModelOutputs ModelOutputs,
        GoldenResultSnapshot Expected);

    private sealed record GoldenSource(
        string FileName,
        string ContentType,
        IReadOnlyList<string>? Lines = null,
        string? AssetName = null);

    private sealed record GoldenModelOutputs(
        DocumentClassification Classification,
        ReceiptData? Receipt = null,
        ShoppingListData? ShoppingList = null,
        SujikoPuzzleData? SujikoPuzzle = null);

    private sealed record GoldenResultSnapshot(
        DocumentCategory Category,
        decimal? ClassificationConfidence,
        string? DocumentTypeDescription,
        bool IsSuccess,
        bool ValidationIsValid,
        HumanReviewStatus HumanReviewStatus,
        bool DocumentDataPresent,
        PolicyDecision? PolicyDecision,
        IReadOnlyList<string> ModelOperations,
        ReceiptData? Receipt,
        ShoppingListData? ShoppingList,
        SujikoPuzzleData? SujikoPuzzle,
        IReadOnlyList<string> ValidationReasons,
        IReadOnlyList<string> HumanReviewReasons,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public static GoldenResultSnapshot FromResult(DocumentProcessingResult result)
        {
            return new GoldenResultSnapshot(
                result.Category,
                result.Classification.Confidence,
                result.Classification.DocumentTypeDescription,
                result.IsSuccess,
                result.Validation.IsValid,
                result.HumanReview.Status,
                result.Receipt is not null
                    || result.ShoppingList is not null
                    || result.SujikoPuzzle is not null
                    || result.ExpenseReport is not null,
                result.PolicyResult?.Decision,
                result.ModelUsage.Calls.Select(call => call.Operation).ToArray(),
                result.Receipt,
                result.ShoppingList,
                result.SujikoPuzzle,
                result.Validation.Reasons.ToArray(),
                result.HumanReview.Reasons.ToArray(),
                result.Errors.ToArray(),
                result.Warnings.ToArray());
        }
    }

    private sealed class GoldenDocumentClassifier(DocumentClassification classification)
        : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                classification,
                new ModelTokenUsage("classification", "golden-classifier", 10, 5, 15)));
        }
    }

    private sealed class GoldenReceiptExtractor(ReceiptData? receipt) : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt ?? throw new InvalidOperationException("The golden case has no receipt output."),
                new ModelTokenUsage("receipt_extraction", "golden-extractor", 20, 10, 30)));
        }
    }

    private sealed class GoldenShoppingListExtractor(ShoppingListData? shoppingList) : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                shoppingList ?? throw new InvalidOperationException("The golden case has no shopping-list output."),
                new ModelTokenUsage("shopping_list_extraction", "golden-extractor", 20, 10, 30)));
        }
    }

    private sealed class GoldenSujikoPuzzleExtractor(SujikoPuzzleData? puzzle) : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                puzzle ?? throw new InvalidOperationException("The golden case has no Sujiko output."),
                new ModelTokenUsage("sujiko_puzzle_extraction", "golden-extractor", 20, 10, 30)));
        }
    }
}

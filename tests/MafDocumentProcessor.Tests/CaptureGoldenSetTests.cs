using System.Text.Json;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureGoldenSetTests
{
    private static readonly string SampleRoot = Path.Combine(
        AppContext.BaseDirectory,
        "next-scenario-samples");

    [Fact]
    public async Task CompositeCaptureCases_MatchManifestSemanticsAndUsage()
    {
        using var manifest = LoadManifest();
        var assets = manifest.RootElement.GetProperty("assets")
            .EnumerateArray()
            .ToDictionary(
                asset => asset.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Asset id was null."),
                StringComparer.Ordinal);

        foreach (var sampleCase in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (sampleCase.GetProperty("kind").GetString() != "CompositeCapture")
            {
                continue;
            }

            var caseId = sampleCase.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Case id was null.");
            var detector = new ManifestRegionDetector(sampleCase, assets);
            var classifier = new QueuedClassifier(ExpectedProcessedCategories(sampleCase));
            var receiptExtractor = new CountingReceiptExtractor();
            var shoppingListExtractor = new CountingShoppingListExtractor();
            var workflow = CreateWorkflow(detector, classifier, receiptExtractor, shoppingListExtractor);
            var request = CreateRequest(sampleCase, assets);

            var result = await workflow.RunAsync(request, CancellationToken.None);

            Assert.True(
                Enum.TryParse(sampleCase.GetProperty("expectedCaptureStatus").GetString(), out CaptureProcessingStatus expectedStatus),
                $"Case {caseId} has an unknown capture status.");
            Assert.Equal(expectedStatus, result.Status);

            var expectedSources = sampleCase.GetProperty("sources").EnumerateArray().ToArray();
            Assert.Equal(expectedSources.Length, result.Sources.Count);
            for (var index = 0; index < expectedSources.Length; index++)
            {
                Assert.True(Enum.TryParse(
                    expectedSources[index].GetProperty("expectedSourceStatus").GetString(),
                    out CaptureProcessingStatus expectedSourceStatus));
                Assert.Equal(expectedSourceStatus, result.Sources[index].Status);
            }

            var expectedDispositions = ExpectedProcessedDispositions(sampleCase);
            var processed = result.Members
                .Where(member => member.Status == CaptureMemberStatus.Processed)
                .ToArray();
            Assert.Equal(expectedDispositions, processed.Select(member => member.Disposition).ToArray());
            Assert.Equal(classifier.Categories.Count, classifier.CallCount);
            Assert.Equal(ValidSourceCount(expectedSources), detector.CallCount);
            Assert.Equal(
                result.ModelUsage.Calls.Count(call => call.Operation == ModelDocumentRegionDetector.Operation),
                detector.CallCount);
            Assert.Equal(
                processed.Length,
                result.ModelUsage.Calls.Count(call => call.Operation == "classification"));
        }
    }

    private static CompositeCaptureWorkflow CreateWorkflow(
        IDocumentRegionDetector detector,
        IDocumentClassifier classifier,
        IReceiptExtractor receiptExtractor,
        IShoppingListExtractor shoppingListExtractor)
    {
        var options = new CompositeCaptureOptions(
            MaxConcurrentSources: 1,
            MaxConcurrentMembers: 1,
            RegionEdgePadding: 0);
        return new CompositeCaptureWorkflow(
            new CaptureSourceDetectionService(new CaptureSourceImageDecoder(options), detector),
            new CaptureRegionValidationService(options),
            classifier,
            receiptExtractor,
            shoppingListExtractor,
            new ReceiptPolicyOptions(),
            options,
            ModelImagePreprocessor.CreateDefault());
    }

    private static CompositeCaptureRequest CreateRequest(
        JsonElement sampleCase,
        IReadOnlyDictionary<string, JsonElement> assets)
    {
        var files = sampleCase.GetProperty("sources")
            .EnumerateArray()
            .Select(source =>
            {
                var assetId = source.GetProperty("assetId").GetString()
                    ?? throw new InvalidOperationException("A capture source asset id was null.");
                var asset = assets[assetId];
                var relativePath = asset.GetProperty("path").GetString()
                    ?? throw new InvalidOperationException($"Asset {assetId} path was null.");
                var content = File.ReadAllBytes(Path.Combine(SampleRoot, relativePath));
                var fileName = Path.GetFileName(relativePath);
                var contentType = asset.GetProperty("contentType").GetString() ?? "image/png";
                return new FileRequest(
                    content,
                    fileName,
                    contentType,
                    content.LongLength,
                    DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
                    "golden-capture");
            })
            .ToArray();

        return CompositeCaptureRequest.Create(files, DateTimeOffset.Parse("2026-08-26T12:00:00Z"), "golden-capture");
    }

    private static IReadOnlyList<DocumentCategory> ExpectedProcessedCategories(JsonElement sampleCase)
    {
        return sampleCase.GetProperty("sources")
            .EnumerateArray()
            .SelectMany(AcceptedRegions)
            .Select(region => ParseCategory(region.GetProperty("expectedClassification").GetString()))
            .Where(category => category is not null)
            .Select(category => category!.Value)
            .ToArray();
    }

    private static IReadOnlyList<CaptureMemberDisposition> ExpectedProcessedDispositions(JsonElement sampleCase)
    {
        return sampleCase.GetProperty("sources")
            .EnumerateArray()
            .SelectMany(AcceptedRegions)
            .Where(region => ParseCategory(region.GetProperty("expectedClassification").GetString()) is not null)
            .Select(region => Enum.Parse<CaptureMemberDisposition>(
                region.GetProperty("expectedDisposition").GetString()
                ?? throw new InvalidOperationException("expectedDisposition was null.")))
            .ToArray();
    }

    private static IEnumerable<JsonElement> AcceptedRegions(JsonElement source)
    {
        return source.GetProperty("regions")
            .EnumerateArray()
            .Where(region =>
            {
                if (!region.TryGetProperty("expectedRegionValidation", out var validation))
                {
                    return region.TryGetProperty("expectedClassification", out var classification)
                        && classification.ValueKind != JsonValueKind.Null;
                }

                return !string.Equals(
                    validation.GetString(),
                    "RejectedDuplicate",
                    StringComparison.Ordinal);
            })
            .OrderBy(region => region.GetProperty("bounds").GetProperty("y").GetDouble())
            .ThenBy(region => region.GetProperty("bounds").GetProperty("x").GetDouble());
    }

    private static int ValidSourceCount(IReadOnlyList<JsonElement> sources)
    {
        return sources.Count(source =>
        {
            if (!source.TryGetProperty("expectedErrorCode", out var code))
            {
                return true;
            }

            var value = code.GetString();
            return !string.Equals(value, "invalid_document_image", StringComparison.Ordinal)
                && !string.Equals(value, "invalid_capture_source", StringComparison.Ordinal);
        });
    }

    private static DocumentCategory? ParseCategory(string? value)
    {
        return value switch
        {
            null => null,
            "Receipt" => DocumentCategory.Receipt,
            "ShoppingList" => DocumentCategory.ShoppingList,
            "SujikoPuzzle" => DocumentCategory.SujikoPuzzle,
            "Unknown" => DocumentCategory.Unknown,
            _ => throw new InvalidOperationException($"Unsupported expected classification '{value}'.")
        };
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(SampleRoot, "manifest.json")));
    }

    private sealed class ManifestRegionDetector(
        JsonElement sampleCase,
        IReadOnlyDictionary<string, JsonElement> assets) : IDocumentRegionDetector
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var fileName = source.Source.Request.FileName;
            var sourceCase = sampleCase.GetProperty("sources")
                .EnumerateArray()
                .Single(candidate =>
                {
                    var assetId = candidate.GetProperty("assetId").GetString();
                    var path = assets[assetId!].GetProperty("path").GetString();
                    return string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase);
                });

            IReadOnlyList<DocumentRegionProposal> proposals = sourceCase.GetProperty("regions")
                .EnumerateArray()
                .Select((region, index) => new DocumentRegionProposal(
                    source.Source.SourceItemId,
                    index + 1,
                    ReadBounds(region.GetProperty("bounds")),
                    outline: null,
                    confidence: region.TryGetProperty("detectionConfidence", out var confidence)
                        ? confidence.GetDecimal()
                        : 0.95m))
                .ToArray();
            return ValueTask.FromResult(new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                proposals,
                new ModelTokenUsage(ModelDocumentRegionDetector.Operation, "golden-detector", 4, 2, 6)));
        }

        private static ProposedNormalizedBounds ReadBounds(JsonElement bounds)
        {
            return new ProposedNormalizedBounds(
                bounds.GetProperty("x").GetDouble(),
                bounds.GetProperty("y").GetDouble(),
                bounds.GetProperty("width").GetDouble(),
                bounds.GetProperty("height").GetDouble());
        }
    }

    private sealed class QueuedClassifier(IReadOnlyList<DocumentCategory> categories) : IDocumentClassifier
    {
        private int _index;

        public IReadOnlyList<DocumentCategory> Categories { get; } = categories;

        public int CallCount { get; private set; }

        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var category = Categories[_index++];
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(category, 0.91m, "golden"),
                new ModelTokenUsage("classification", "golden-classifier", 3, 1, 4)));
        }
    }

    private sealed class CountingReceiptExtractor : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData("North Star Cafe", 10.50m, new DateOnly(2026, 8, 20), "Visa", "GBP"),
                new ModelTokenUsage("receipt_extraction", "golden-receipt", 5, 2, 7)));
        }
    }

    private sealed class CountingShoppingListExtractor : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                new ShoppingListData("Weekly shopping", [new ShoppingListItem("milk", 2, "pints", false)], null),
                new ModelTokenUsage("shopping_list_extraction", "golden-list", 5, 2, 7)));
        }
    }
}

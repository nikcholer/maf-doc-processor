using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;

namespace MafDocumentProcessor.Tests;

public sealed class NextScenarioSampleSetTests
{
    private static readonly string SampleRoot = Path.Combine(
        AppContext.BaseDirectory,
        "next-scenario-samples");

    [Fact]
    public void Manifest_ContainsVersionedNonConfidentialAssetsAndRequiredCases()
    {
        using var manifest = LoadManifest();
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(10, assets.Length);
        Assert.Equal(13, cases.Length);
        Assert.Equal(
            assets.Length,
            assets.Select(GetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            cases.Length,
            cases.Select(GetId).Distinct(StringComparer.Ordinal).Count());

        var assetIds = assets.Select(GetId).ToHashSet(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            Assert.False(asset.GetProperty("containsPersonalData").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("origin").GetString()));

            var relativePath = asset.GetProperty("path").GetString()
                ?? throw new InvalidOperationException("A sample asset path was null.");
            var assetPath = Path.Combine(SampleRoot, relativePath);
            Assert.True(File.Exists(assetPath), $"Missing sample asset: {relativePath}");

            var actualHash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(assetPath)))
                .ToLowerInvariant();
            Assert.Equal(asset.GetProperty("sha256").GetString(), actualHash);

            if (asset.GetProperty("contentType").GetString() == "image/png")
            {
                var image = Image.Identify(assetPath);
                Assert.Equal(asset.GetProperty("width").GetInt32(), image.Width);
                Assert.Equal(asset.GetProperty("height").GetInt32(), image.Height);
            }

            AssertReferencedProvenanceFileExists(asset, "definition");
            AssertReferencedProvenanceFileExists(asset, "generationNotes");
        }

        foreach (var sampleCase in cases)
        {
            var kind = sampleCase.GetProperty("kind").GetString();
            if (kind == "CompositeCapture")
            {
                foreach (var source in sampleCase.GetProperty("sources").EnumerateArray())
                {
                    var sourceAssetId = source.GetProperty("assetId").GetString()
                        ?? throw new InvalidOperationException("A capture source asset id was null.");
                    Assert.Contains(sourceAssetId, assetIds);
                    foreach (var region in source.GetProperty("regions").EnumerateArray())
                    {
                        AssertValidNormalizedBounds(region.GetProperty("bounds"));
                    }
                }
            }
            else
            {
                Assert.Equal("ExpenseReport", kind);
                var expenseAssetId = sampleCase.GetProperty("assetId").GetString()
                    ?? throw new InvalidOperationException("An expense-report asset id was null.");
                Assert.Contains(expenseAssetId, assetIds);
            }
        }

        var coverage = cases
            .SelectMany(sampleCase => sampleCase.GetProperty("covers").EnumerateArray())
            .Select(value => value.GetString()
                ?? throw new InvalidOperationException("A sample coverage value was null."))
            .ToHashSet(StringComparer.Ordinal);
        AssertRequiredCoverage(coverage);
    }

    private static JsonDocument LoadManifest()
    {
        var path = Path.Combine(SampleRoot, "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find next-scenario manifest at {path}.", path);
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string GetId(JsonElement element)
    {
        return element.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("A sample id was null.");
    }

    private static void AssertReferencedProvenanceFileExists(
        JsonElement asset,
        string propertyName)
    {
        if (!asset.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        var relativePath = property.GetString()
            ?? throw new InvalidOperationException($"Asset {propertyName} was null.");
        Assert.True(
            File.Exists(Path.Combine(SampleRoot, relativePath)),
            $"Missing sample provenance file: {relativePath}");
    }

    private static void AssertValidNormalizedBounds(JsonElement bounds)
    {
        var x = bounds.GetProperty("x").GetDouble();
        var y = bounds.GetProperty("y").GetDouble();
        var width = bounds.GetProperty("width").GetDouble();
        var height = bounds.GetProperty("height").GetDouble();

        Assert.True(double.IsFinite(x) && x >= 0 && x < 1);
        Assert.True(double.IsFinite(y) && y >= 0 && y < 1);
        Assert.True(double.IsFinite(width) && width > 0 && width <= 1);
        Assert.True(double.IsFinite(height) && height > 0 && height <= 1);
        Assert.True(x + width <= 1.000001);
        Assert.True(y + height <= 1.000001);
    }

    private static void AssertRequiredCoverage(IReadOnlySet<string> coverage)
    {
        string[] required =
        [
            "single-source",
            "single-document",
            "multi-document",
            "multi-source",
            "overlap",
            "duplicate",
            "unsupported-member",
            "source-failure",
            "partial-success",
            "review",
            "valid",
            "invalid",
            "repairable"
        ];

        foreach (var requirement in required)
        {
            Assert.Contains(requirement, coverage);
        }
    }
}

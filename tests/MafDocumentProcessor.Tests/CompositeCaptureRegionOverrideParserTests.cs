using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;

namespace MafDocumentProcessor.Tests;

public sealed class CompositeCaptureRegionOverrideParserTests
{
    private static readonly CompositeCaptureOptions Options = new(MaxDetectedRegionsPerSource: 2);

    [Fact]
    public void Parse_MissingField_LeavesEverySourceOnAutomaticDetection()
    {
        var result = CompositeCaptureRegionOverrideParser.Parse(null, 2, Options);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Overrides);
    }

    [Fact]
    public void Parse_ValidPartialOverrides_PreservesGeometryAndExplicitEmptySources()
    {
        var result = CompositeCaptureRegionOverrideParser.Parse(
            """
            {
              "sources": [
                {
                  "sourceIndex": 1,
                  "regions": [
                    {
                      "bounds": { "x": 0.1, "y": 0.2, "width": 0.4, "height": 0.5 },
                      "outline": [
                        { "x": 0.1, "y": 0.2 },
                        { "x": 0.5, "y": 0.2 },
                        { "x": 0.5, "y": 0.7 },
                        { "x": 0.1, "y": 0.7 }
                      ]
                    }
                  ]
                },
                { "sourceIndex": 2, "regions": [] }
              ]
            }
            """,
            3,
            Options);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Overrides?.Count);
        var first = Assert.Single(result.Overrides![1]);
        Assert.Equal(0.1, first.Bounds.X);
        Assert.Equal(4, first.Outline?.Count);
        Assert.Empty(result.Overrides[2]);
        Assert.False(result.Overrides.ContainsKey(3));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"sources\":null}")]
    [InlineData("{\"sources\":[{\"sourceIndex\":0,\"regions\":[]}]}")]
    [InlineData("{\"sources\":[{\"sourceIndex\":3,\"regions\":[]}]}")]
    [InlineData("{\"sources\":[{\"sourceIndex\":1,\"regions\":[]},{\"sourceIndex\":1,\"regions\":[]}]}")]
    [InlineData("{\"sources\":[{\"sourceIndex\":1,\"regions\":[{\"bounds\":null}]}]}")]
    [InlineData("{\"sources\":[{\"sourceIndex\":1,\"regions\":[{\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"outline\":[{\"x\":0,\"y\":0}]}]}]}")]
    public void Parse_InvalidStructures_ReturnARequestBoundaryFailure(string json)
    {
        var result = CompositeCaptureRegionOverrideParser.Parse(json, 2, Options);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Overrides);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_TooManyRegions_ReturnsARequestBoundaryFailure()
    {
        const string bounds = "{\"bounds\":{\"x\":0.1,\"y\":0.1,\"width\":0.2,\"height\":0.2}}";
        var json = $"{{\"sources\":[{{\"sourceIndex\":1,\"regions\":[{bounds},{bounds},{bounds}]}}]}}";

        var result = CompositeCaptureRegionOverrideParser.Parse(json, 1, Options);

        Assert.False(result.IsSuccess);
        Assert.Contains("at most 2", result.Error, StringComparison.Ordinal);
    }
}

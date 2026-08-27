using Microsoft.AspNetCore.Mvc.Testing;

namespace MafDocumentProcessor.Tests;

public sealed class DemoUiIntegrationTests
{
    [Fact]
    public async Task Index_ExposesIndividualAndAccessibleCaptureModes()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("value=\"single\" checked", html, StringComparison.Ordinal);
        Assert.Contains("value=\"capture\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sourceGrid\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"memberInspector\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("/capture-ui.js?v=20260827.2", html, StringComparison.Ordinal);
        Assert.Contains("/app.js?v=20260827.2", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiAssets_ExposeEphemeralRegionCorrectionAndReprocessing()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var app = await client.GetStringAsync("/app.js");
        var captureUi = await client.GetStringAsync("/capture-ui.js");

        Assert.Contains("regionOverrides", app, StringComparison.Ordinal);
        Assert.Contains("pointerdown", app, StringComparison.Ordinal);
        Assert.Contains("Save and reprocess", app, StringComparison.Ordinal);
        Assert.Contains("cancelRegionEdit", app, StringComparison.Ordinal);
        Assert.Contains("aria-modal", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Finish editing", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Reprocess corrected regions", await client.GetStringAsync("/"), StringComparison.Ordinal);
        Assert.Contains("resizeBounds", captureUi, StringComparison.Ordinal);
        Assert.Contains("reorderRegions", captureUi, StringComparison.Ordinal);
        Assert.Contains("createRegionEditSession", captureUi, StringComparison.Ordinal);
        Assert.Contains("hasRegionChanges", captureUi, StringComparison.Ordinal);
        Assert.Contains("serializeRegionOverrides", captureUi, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/capture-ui.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    public async Task UiAssets_AreServed(string path, string expectedMediaType)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal(expectedMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }
}

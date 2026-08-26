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
        Assert.Contains("/capture-ui.js", html, StringComparison.Ordinal);
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
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }
}

using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (AiModelSettings settings) =>
        {
            var apiKeyConfigured =
                ApiKeyEnvironment.HasApiKey(settings.DocumentClassification.ApiKeyEnvironmentVariable)
                && ApiKeyEnvironment.HasApiKey(settings.DocumentExtraction.ApiKeyEnvironmentVariable)
                && ApiKeyEnvironment.HasApiKey(settings.TextTesting.ApiKeyEnvironmentVariable);

            return Results.Ok(new HealthResponse(
                apiKeyConfigured ? "ready" : "missing_api_key",
                settings.DocumentClassification.Provider,
                settings.DocumentClassification.ModelId,
                settings.TextTesting.ModelId,
                apiKeyConfigured));
        });

        return endpoints;
    }
}

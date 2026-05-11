namespace MafDocumentProcessor.Api.Contracts;

public sealed record HealthResponse(
    string Status,
    string AiProvider,
    string ImageModel,
    string TextModel,
    bool ApiKeyConfigured);

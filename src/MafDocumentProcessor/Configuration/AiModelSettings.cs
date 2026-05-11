namespace MafDocumentProcessor.Configuration;

public sealed record ModelRoleSettings(
    string Provider,
    string Endpoint,
    string ModelId,
    string ApiKeyEnvironmentVariable,
    string ServiceId,
    int RequestTimeoutSeconds = 600,
    decimal? InputTokenPricePerMillionUsd = null,
    decimal? OutputTokenPricePerMillionUsd = null);

public sealed record AiModelSettings(
    ModelRoleSettings ImageRecognition,
    ModelRoleSettings TextTesting);

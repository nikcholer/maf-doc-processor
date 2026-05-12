namespace MafDocumentProcessor.Configuration;

public sealed record ModelRoleSettings(
    string Provider,
    string Endpoint,
    string ModelId,
    string ApiKeyEnvironmentVariable,
    string ServiceId,
    int RequestTimeoutSeconds = 60,
    int MaxRetryAttempts = 2,
    int RetryBaseDelayMilliseconds = 500,
    decimal? InputTokenPricePerMillionUsd = null,
    decimal? OutputTokenPricePerMillionUsd = null);

public sealed record AiModelSettings(
    ModelRoleSettings DocumentClassification,
    ModelRoleSettings DocumentExtraction,
    ModelRoleSettings TextTesting)
{
    public ModelRoleSettings ImageRecognition => DocumentClassification;
}

namespace MafDocumentProcessor.Configuration;

public sealed record ModelRoleSettings(
    string Provider,
    string Endpoint,
    string ModelId,
    string ApiKeyEnvironmentVariable,
    string ServiceId,
    int RequestTimeoutSeconds = 180);

public sealed record AiModelSettings(
    ModelRoleSettings ImageRecognition,
    ModelRoleSettings TextTesting);

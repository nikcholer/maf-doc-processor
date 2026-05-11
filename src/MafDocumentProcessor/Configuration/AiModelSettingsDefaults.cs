namespace MafDocumentProcessor.Configuration;

public static class AiModelSettingsDefaults
{
    public const string TogetherAiProvider = "TogetherAI";
    public const string TogetherAiEndpoint = "https://api.together.ai/v1";
    public const string TogetherAiApiKeyEnvironmentVariable = "TOGETHER_API_KEY";
    public const string TogetherGemma4ModelId = "google/gemma-4-31B-it";
    public const decimal TogetherGemma4InputTokenPricePerMillionUsd = 0.20m;
    public const decimal TogetherGemma4OutputTokenPricePerMillionUsd = 0.50m;

    public static AiModelSettings CreateTogetherGemma4()
    {
        return new AiModelSettings(
            CreateTogetherGemma4Role("image-recognition"),
            CreateTogetherGemma4Role("text-testing"));
    }

    public static ModelRoleSettings CreateTogetherGemma4Role(string serviceId)
    {
        return new ModelRoleSettings(
            TogetherAiProvider,
            TogetherAiEndpoint,
            TogetherGemma4ModelId,
            TogetherAiApiKeyEnvironmentVariable,
            serviceId,
            InputTokenPricePerMillionUsd: TogetherGemma4InputTokenPricePerMillionUsd,
            OutputTokenPricePerMillionUsd: TogetherGemma4OutputTokenPricePerMillionUsd);
    }
}

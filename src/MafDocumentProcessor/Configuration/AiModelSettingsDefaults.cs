namespace MafDocumentProcessor.Configuration;

public static class AiModelSettingsDefaults
{
    public const string TogetherAiProvider = "TogetherAI";
    public const string TogetherAiEndpoint = "https://api.together.ai/v1";
    public const string TogetherAiApiKeyEnvironmentVariable = "TOGETHER_API_KEY";
    public const string TogetherGemma4ModelId = "google/gemma-4-31B-it";
    public const decimal TogetherGemma4InputTokenPricePerMillionUsd = 0.20m;
    public const decimal TogetherGemma4OutputTokenPricePerMillionUsd = 0.50m;
    public const string TogetherQwen35NineBModelId = "Qwen/Qwen3.5-9B";
    public const decimal TogetherQwen35NineBInputTokenPricePerMillionUsd = 0.10m;
    public const decimal TogetherQwen35NineBOutputTokenPricePerMillionUsd = 0.15m;

    public static AiModelSettings CreateTogetherDefaults()
    {
        return new AiModelSettings(
            CreateTogetherQwen35NineBRole("document-classification"),
            CreateTogetherQwen35NineBRole("document-extraction"),
            CreateTogetherGemma4Role("text-testing"));
    }

    public static AiModelSettings CreateTogetherGemma4()
    {
        return CreateTogetherDefaults();
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

    public static ModelRoleSettings CreateTogetherQwen35NineBRole(string serviceId)
    {
        return new ModelRoleSettings(
            TogetherAiProvider,
            TogetherAiEndpoint,
            TogetherQwen35NineBModelId,
            TogetherAiApiKeyEnvironmentVariable,
            serviceId,
            InputTokenPricePerMillionUsd: TogetherQwen35NineBInputTokenPricePerMillionUsd,
            OutputTokenPricePerMillionUsd: TogetherQwen35NineBOutputTokenPricePerMillionUsd);
    }
}

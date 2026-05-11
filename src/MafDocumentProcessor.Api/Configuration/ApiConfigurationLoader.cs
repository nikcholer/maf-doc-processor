using MafDocumentProcessor.Configuration;

namespace MafDocumentProcessor.Api.Configuration;

public static class ApiConfigurationLoader
{
    private const string AiModelsSectionName = "AiModels";
    private const string DocumentIntakeSectionName = "DocumentIntake";
    private const string ReceiptPolicySectionName = "ReceiptPolicy";

    public static AiModelSettings LoadAiModelSettings(IConfiguration configuration)
    {
        var defaults = AiModelSettingsDefaults.CreateTogetherGemma4();
        var section = configuration.GetSection(AiModelsSectionName);

        return new AiModelSettings(
            LoadModelRole(section.GetSection(nameof(AiModelSettings.ImageRecognition)), defaults.ImageRecognition),
            LoadModelRole(section.GetSection(nameof(AiModelSettings.TextTesting)), defaults.TextTesting));
    }

    public static DocumentIntakeSettings LoadDocumentIntakeSettings(IConfiguration configuration)
    {
        var defaults = new DocumentIntakeSettings();
        var section = configuration.GetSection(DocumentIntakeSectionName);

        return new DocumentIntakeSettings
        {
            ImageFormFieldName = section.GetValue<string>(nameof(DocumentIntakeSettings.ImageFormFieldName))
                ?? defaults.ImageFormFieldName,
            MaxUploadBytes = section.GetValue<long?>(nameof(DocumentIntakeSettings.MaxUploadBytes))
                ?? defaults.MaxUploadBytes,
            AllowedContentTypes = section.GetSection(nameof(DocumentIntakeSettings.AllowedContentTypes))
                .Get<string[]>()
                ?? defaults.AllowedContentTypes,
            AllowedExtensions = section.GetSection(nameof(DocumentIntakeSettings.AllowedExtensions))
                .Get<string[]>()
                ?? defaults.AllowedExtensions
        };
    }

    public static ReceiptPolicyOptions LoadReceiptPolicyOptions(IConfiguration configuration)
    {
        var defaults = new ReceiptPolicyOptions();
        var section = configuration.GetSection(ReceiptPolicySectionName);

        return new ReceiptPolicyOptions(
            section.GetValue<decimal?>(nameof(ReceiptPolicyOptions.ReviewThreshold))
                ?? defaults.ReviewThreshold,
            section.GetValue<string>(nameof(ReceiptPolicyOptions.DefaultCurrencyCode))
                ?? defaults.DefaultCurrencyCode);
    }

    private static ModelRoleSettings LoadModelRole(
        IConfiguration section,
        ModelRoleSettings defaults)
    {
        return new ModelRoleSettings(
            section.GetValue<string>(nameof(ModelRoleSettings.Provider)) ?? defaults.Provider,
            section.GetValue<string>(nameof(ModelRoleSettings.Endpoint)) ?? defaults.Endpoint,
            section.GetValue<string>(nameof(ModelRoleSettings.ModelId)) ?? defaults.ModelId,
            section.GetValue<string>(nameof(ModelRoleSettings.ApiKeyEnvironmentVariable))
                ?? defaults.ApiKeyEnvironmentVariable,
            section.GetValue<string>(nameof(ModelRoleSettings.ServiceId)) ?? defaults.ServiceId,
            section.GetValue<int?>(nameof(ModelRoleSettings.RequestTimeoutSeconds))
                ?? defaults.RequestTimeoutSeconds,
            section.GetValue<decimal?>(nameof(ModelRoleSettings.InputTokenPricePerMillionUsd))
                ?? defaults.InputTokenPricePerMillionUsd,
            section.GetValue<decimal?>(nameof(ModelRoleSettings.OutputTokenPricePerMillionUsd))
                ?? defaults.OutputTokenPricePerMillionUsd);
    }
}

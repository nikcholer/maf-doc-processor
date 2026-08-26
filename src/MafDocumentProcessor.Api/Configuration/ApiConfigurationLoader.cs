using MafDocumentProcessor.Configuration;

namespace MafDocumentProcessor.Api.Configuration;

public static class ApiConfigurationLoader
{
    private const string AiModelsSectionName = "AiModels";
    private const string ModelImagePreprocessingSectionName = "ModelImagePreprocessing";
    private const string DocumentIntakeSectionName = "DocumentIntake";
    private const string CompositeCaptureSectionName = "CompositeCapture";
    private const string ReceiptPolicySectionName = "ReceiptPolicy";
    private const string ExpensePolicySectionName = "ExpensePolicy";

    public static AiModelSettings LoadAiModelSettings(IConfiguration configuration)
    {
        var defaults = AiModelSettingsDefaults.CreateTogetherDefaults();
        var section = configuration.GetSection(AiModelsSectionName);
        var legacyImageRecognition = section.GetSection("ImageRecognition");
        var classificationDefaults = legacyImageRecognition.Exists()
            ? LoadModelRole(legacyImageRecognition, defaults.DocumentClassification)
            : defaults.DocumentClassification;
        var extractionDefaults = legacyImageRecognition.Exists()
            ? LoadModelRole(legacyImageRecognition, defaults.DocumentExtraction)
            : defaults.DocumentExtraction;

        return new AiModelSettings(
            LoadModelRole(section.GetSection(nameof(AiModelSettings.DocumentClassification)), classificationDefaults),
            LoadModelRole(section.GetSection(nameof(AiModelSettings.DocumentExtraction)), extractionDefaults),
            LoadModelRole(section.GetSection(nameof(AiModelSettings.TextTesting)), defaults.TextTesting),
            LoadModelRole(
                section.GetSection(nameof(AiModelSettings.DocumentRegionDetection)),
                defaults.DocumentRegionDetection));
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

    public static CompositeCaptureOptions LoadCompositeCaptureOptions(IConfiguration configuration)
    {
        var defaults = new CompositeCaptureOptions();
        var section = configuration.GetSection(CompositeCaptureSectionName);

        return new CompositeCaptureOptions(
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxSourceCount))
                ?? defaults.MaxSourceCount,
            section.GetValue<long?>(nameof(CompositeCaptureOptions.MaxSourceBytes))
                ?? defaults.MaxSourceBytes,
            section.GetValue<long?>(nameof(CompositeCaptureOptions.MaxAggregateBytes))
                ?? defaults.MaxAggregateBytes,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxSourceWidthPixels))
                ?? defaults.MaxSourceWidthPixels,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxSourceHeightPixels))
                ?? defaults.MaxSourceHeightPixels,
            section.GetValue<long?>(nameof(CompositeCaptureOptions.MaxSourcePixelCount))
                ?? defaults.MaxSourcePixelCount,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxDetectedRegionsPerSource))
                ?? defaults.MaxDetectedRegionsPerSource,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxMembersPerCapture))
                ?? defaults.MaxMembersPerCapture,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.MinRegionWidth))
                ?? defaults.MinRegionWidth,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.MinRegionHeight))
                ?? defaults.MinRegionHeight,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.MinRegionArea))
                ?? defaults.MinRegionArea,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.DuplicateIntersectionOverUnionThreshold))
                ?? defaults.DuplicateIntersectionOverUnionThreshold,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.OverlapReviewIntersectionOverUnionThreshold))
                ?? defaults.OverlapReviewIntersectionOverUnionThreshold,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxConcurrentSources))
                ?? defaults.MaxConcurrentSources,
            section.GetValue<int?>(nameof(CompositeCaptureOptions.MaxConcurrentMembers))
                ?? defaults.MaxConcurrentMembers,
            section.GetValue<double?>(nameof(CompositeCaptureOptions.RegionEdgePadding))
                ?? defaults.RegionEdgePadding)
            .Validate();
    }

    public static ModelImagePreprocessingSettings LoadModelImagePreprocessingSettings(
        IConfiguration configuration)
    {
        var defaults = new ModelImagePreprocessingSettings();
        var section = configuration.GetSection(ModelImagePreprocessingSectionName);

        return new ModelImagePreprocessingSettings(
            section.GetValue<bool?>(nameof(ModelImagePreprocessingSettings.Enabled))
                ?? defaults.Enabled,
            section.GetValue<int?>(nameof(ModelImagePreprocessingSettings.ClassificationMaxLongEdgePixels))
                ?? defaults.ClassificationMaxLongEdgePixels,
            section.GetValue<int?>(nameof(ModelImagePreprocessingSettings.ExtractionMaxLongEdgePixels))
                ?? defaults.ExtractionMaxLongEdgePixels,
            section.GetValue<int?>(nameof(ModelImagePreprocessingSettings.JpegQuality))
                ?? defaults.JpegQuality,
            section.GetValue<int?>(nameof(ModelImagePreprocessingSettings.RegionDetectionMaxLongEdgePixels))
                ?? defaults.RegionDetectionMaxLongEdgePixels);
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

    public static ExpensePolicyOptions LoadExpensePolicyOptions(IConfiguration configuration)
    {
        var defaults = new ExpensePolicyOptions();
        var section = configuration.GetSection(ExpensePolicySectionName);

        return new ExpensePolicyOptions(
            section.GetValue<decimal?>(nameof(ExpensePolicyOptions.HighValueReviewThreshold))
                ?? defaults.HighValueReviewThreshold);
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
            section.GetValue<int?>(nameof(ModelRoleSettings.MaxRetryAttempts))
                ?? defaults.MaxRetryAttempts,
            section.GetValue<int?>(nameof(ModelRoleSettings.RetryBaseDelayMilliseconds))
                ?? defaults.RetryBaseDelayMilliseconds,
            section.GetValue<decimal?>(nameof(ModelRoleSettings.InputTokenPricePerMillionUsd))
                ?? defaults.InputTokenPricePerMillionUsd,
            section.GetValue<decimal?>(nameof(ModelRoleSettings.OutputTokenPricePerMillionUsd))
                ?? defaults.OutputTokenPricePerMillionUsd);
    }
}

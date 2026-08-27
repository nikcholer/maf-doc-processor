using System.Text.Json.Serialization;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Endpoints;
using MafDocumentProcessor.Api.OpenApi;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
var intakeSettings = ApiConfigurationLoader.LoadDocumentIntakeSettings(builder.Configuration);
var captureOptions = ApiConfigurationLoader.LoadCompositeCaptureOptions(builder.Configuration);
var maxRequestBodyBytes = Math.Max(intakeSettings.MaxUploadBytes, captureOptions.MaxAggregateBytes) + 256 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodyBytes;
});
builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<ProcessedDocumentDataSchemaTransformer>();
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(intakeSettings);
builder.Services.AddSingleton(captureOptions);
builder.Services.AddSingleton(ApiConfigurationLoader.LoadAiModelSettings(builder.Configuration));
builder.Services.AddSingleton(ApiConfigurationLoader.LoadModelImagePreprocessingSettings(builder.Configuration));
builder.Services.AddSingleton(ApiConfigurationLoader.LoadReceiptPolicyOptions(builder.Configuration));
builder.Services.AddSingleton(ApiConfigurationLoader.LoadExpensePolicyOptions(builder.Configuration));
builder.Services.AddSingleton<DocumentImageValidator>();
builder.Services.AddSingleton<IModelImagePreprocessor, ModelImagePreprocessor>();
builder.Services.AddSingleton<ICaptureSourceImageDecoder, CaptureSourceImageDecoder>();
builder.Services.AddSingleton<ICaptureDetectionImagePreparer, CaptureDetectionImagePreparer>();
builder.Services.AddSingleton<IModelChatClient, OpenAICompatibleModelChatClient>();
builder.Services.AddScoped<IDocumentRegionDetector>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelDocumentRegionDetector(
        sp.GetRequiredService<IModelChatClient>(),
        sp.GetRequiredService<ICaptureDetectionImagePreparer>(),
        settings.DocumentRegionDetection,
        sp.GetRequiredService<CompositeCaptureOptions>());
});
builder.Services.AddScoped<ICaptureSourceDetectionService, CaptureSourceDetectionService>();
builder.Services.AddSingleton<ICaptureRegionValidationService, CaptureRegionValidationService>();
builder.Services.AddScoped<IDocumentClassifier>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelDocumentClassifier(
        sp.GetRequiredService<IModelChatClient>(),
        settings.DocumentClassification);
});
builder.Services.AddScoped<IReceiptExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelReceiptExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.DocumentExtraction);
});
builder.Services.AddScoped<IShoppingListExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelShoppingListExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.DocumentExtraction);
});
builder.Services.AddScoped<ISujikoPuzzleExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelSujikoPuzzleExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.DocumentExtraction);
});
builder.Services.AddScoped<IExpenseReportExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelExpenseReportExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.DocumentExtraction);
});
builder.Services.AddScoped<DocumentProcessingWorkflow>();
builder.Services.AddScoped<CompositeCaptureWorkflow>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store, max-age=0";
    }
});

app.MapOpenApi();
app.MapHealthEndpoints();
app.MapDocumentProcessingEndpoints();
app.MapDocumentCaptureEndpoints();

app.Run();

public partial class Program;

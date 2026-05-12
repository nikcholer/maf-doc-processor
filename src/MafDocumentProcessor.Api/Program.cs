using System.Text.Json.Serialization;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Endpoints;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

var builder = WebApplication.CreateBuilder(args);
var intakeSettings = ApiConfigurationLoader.LoadDocumentIntakeSettings(builder.Configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = intakeSettings.MaxUploadBytes + 64 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(intakeSettings);
builder.Services.AddSingleton(ApiConfigurationLoader.LoadAiModelSettings(builder.Configuration));
builder.Services.AddSingleton(ApiConfigurationLoader.LoadModelImagePreprocessingSettings(builder.Configuration));
builder.Services.AddSingleton(ApiConfigurationLoader.LoadReceiptPolicyOptions(builder.Configuration));
builder.Services.AddSingleton<DocumentImageValidator>();
builder.Services.AddSingleton<IModelImagePreprocessor, ModelImagePreprocessor>();
builder.Services.AddSingleton<IModelChatClient, OpenAICompatibleModelChatClient>();
builder.Services.AddScoped<IDocumentClassifier>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelDocumentClassifier(
        sp.GetRequiredService<IModelChatClient>(),
        settings.ImageRecognition);
});
builder.Services.AddScoped<IReceiptExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelReceiptExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.ImageRecognition);
});
builder.Services.AddScoped<IShoppingListExtractor>(sp =>
{
    var settings = sp.GetRequiredService<AiModelSettings>();
    return new ModelShoppingListExtractor(
        sp.GetRequiredService<IModelChatClient>(),
        settings.ImageRecognition);
});
builder.Services.AddScoped<DocumentProcessingWorkflow>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapDocumentProcessingEndpoints();

app.Run();

public partial class Program;

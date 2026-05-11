using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;

namespace MafDocumentProcessor.Tests;

public sealed class ModelDocumentServicesTests
{
    [Fact]
    public async Task ClassifyAsync_UsesConfiguredImageRoleAndParsesJson()
    {
        var chatClient = new CapturingModelChatClient(
            """{"category":"Receipt","confidence":0.87,"confidenceReasoning":"receipt layout"}""");
        var settings = AiModelSettingsDefaults.CreateTogetherGemma4Role("image-recognition");
        var classifier = new ModelDocumentClassifier(chatClient, settings);

        var result = await classifier.ClassifyAsync(CreateReceiptRequest(), CancellationToken.None);

        Assert.Equal(DocumentCategory.Receipt, result.Value.Category);
        Assert.Equal(0.87m, result.Value.Confidence);
        Assert.Equal("google/gemma-4-31B-it", result.Usage.ModelId);
        Assert.Equal("classification", chatClient.LastRequest?.Operation);
        Assert.Equal(settings, chatClient.LastRequest?.Settings);
        Assert.Contains(
            chatClient.LastRequest!.Messages.SelectMany(message => message.Content),
            part => part is ModelImageContent);
    }

    [Fact]
    public async Task ExtractReceiptAsync_UsesConfiguredRoleAndParsesJson()
    {
        var chatClient = new CapturingModelChatClient(
            """{"storeName":"Corner Shop","totalAmount":12.34,"purchaseDate":"2026-05-11","paymentMethod":"Visa","currencyCode":"gbp"}""");
        var settings = AiModelSettingsDefaults.CreateTogetherGemma4Role("text-testing");
        var extractor = new ModelReceiptExtractor(chatClient, settings);

        var result = await extractor.ExtractReceiptAsync(CreateReceiptRequest(), CancellationToken.None);

        Assert.Equal("Corner Shop", result.Value.StoreName);
        Assert.Equal(12.34m, result.Value.TotalAmount);
        Assert.Equal("GBP", result.Value.CurrencyCode);
        Assert.Equal("receipt_extraction", chatClient.LastRequest?.Operation);
        Assert.Equal(settings, chatClient.LastRequest?.Settings);
    }

    [Fact]
    public void CreateTogetherGemma4_PreservesSeparateRoleSettings()
    {
        var settings = AiModelSettingsDefaults.CreateTogetherGemma4();

        Assert.Equal("google/gemma-4-31B-it", settings.ImageRecognition.ModelId);
        Assert.Equal("google/gemma-4-31B-it", settings.TextTesting.ModelId);
        Assert.NotEqual(settings.ImageRecognition.ServiceId, settings.TextTesting.ServiceId);
        Assert.Equal("TOGETHER_API_KEY", settings.ImageRecognition.ApiKeyEnvironmentVariable);
        Assert.Equal("TOGETHER_API_KEY", settings.TextTesting.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public async Task OpenAICompatibleModelChatClient_RequiresConfiguredApiKey()
    {
        var missingEnvironmentVariable = $"MISSING_TOGETHER_KEY_{Guid.NewGuid():N}";
        var settings = AiModelSettingsDefaults.CreateTogetherGemma4Role("image-recognition") with
        {
            ApiKeyEnvironmentVariable = missingEnvironmentVariable
        };
        var client = new OpenAICompatibleModelChatClient();

        var exception = await Assert.ThrowsAsync<ModelConfigurationException>(async () =>
            await client.CompleteAsync(
                new ModelChatRequest(
                    "classification",
                    settings,
                    [ModelChatMessage.CreateUser(new ModelTextContent("hello"))],
                    MaxOutputTokens: 20),
                CancellationToken.None));

        Assert.Contains(missingEnvironmentVariable, exception.Message);
    }

    private static FileRequest CreateReceiptRequest()
    {
        return new FileRequest(
            [1, 2, 3],
            "receipt.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            SourceId: "unit-test");
    }

    private sealed class CapturingModelChatClient(string response) : IModelChatClient
    {
        public ModelChatRequest? LastRequest { get; private set; }

        public ValueTask<ModelChatResponse> CompleteAsync(
            ModelChatRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(new ModelChatResponse(
                response,
                new ModelTokenUsage(request.Operation, request.Settings.ModelId, 10, 20, 30)));
        }
    }
}

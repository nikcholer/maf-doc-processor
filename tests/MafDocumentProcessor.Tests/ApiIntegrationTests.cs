using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Api.Endpoints;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace MafDocumentProcessor.Tests;

public sealed class ApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Health_ReturnsReadyWhenTestApiKeyIsConfigured()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.NotNull(response);
        Assert.Equal("ready", response.Status);
        Assert.True(response.ApiKeyConfigured);
        Assert.Equal("TogetherAI", response.AiProvider);
    }

    [Fact]
    public async Task ProcessDocument_WithReceiptImage_ReturnsMappedResponse()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal(DocumentCategory.Receipt, body.Category);
        var data = Assert.IsType<JsonElement>(body.Document?.Data);
        Assert.Equal("Meadow Vale Supermarket", data.Deserialize<ReceiptData>(JsonOptions)?.StoreName);
        Assert.Equal(2, body.ModelUsage.Calls.Count);
        Assert.Equal(15, body.ModelUsage.TotalTokens);
    }

    [Fact]
    public async Task ProcessDocument_WithUnsupportedContentType_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.txt", "text/plain", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("invalid_document_upload", body.Code);
        Assert.Equal("image", body.Target);
        Assert.Contains("Unsupported content type", body.Message);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
    }

    [Fact]
    public async Task ProcessDocument_WhenModelResponseIsInvalid_ReturnsBadGatewayContract()
    {
        using var factory = new ApiIntegrationTestFactory(
            receiptExtractor: new ThrowingReceiptExtractor(
                new DocumentModelResponseException("The receipt extraction model returned invalid JSON.")));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("model_response_invalid", body.Code);
        Assert.Null(body.Target);
        Assert.Contains("invalid JSON", body.Message);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
    }

    [Fact]
    public async Task ProcessDocument_WhenRequestIsCanceled_PropagatesCancellationToModelCall()
    {
        var extractor = new CancellationObservingReceiptExtractor();
        using var cancellation = new CancellationTokenSource();
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(),
            extractor,
            new FakeShoppingListExtractor(),
            new ReceiptPolicyOptions(),
            new PassThroughImagePreprocessor());
        var request = CreateHttpRequestWithFormFile("receipt.png", "image/png", [1, 2, 3]);

        var requestTask = DocumentProcessingEndpoints.ProcessDocumentAsync(
            request,
            new DocumentIntakeSettings(),
            ApiIntegrationTestFactory.CreateTestModelSettings(),
            new DocumentImageValidator(),
            workflow,
            NullLoggerFactory.Instance,
            cancellation.Token);
        await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Assert.True(await extractor.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static MultipartFormDataContent CreateMultipartImageContent(
        string fileName,
        string contentType,
        byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "image", fileName);
        return content;
    }

    private static HttpRequest CreateHttpRequestWithFormFile(
        string fileName,
        string contentType,
        byte[] bytes)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test-boundary";

        var file = new FormFile(
            new MemoryStream(bytes),
            baseStreamOffset: 0,
            length: bytes.Length,
            name: "image",
            fileName: fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        var files = new FormFileCollection { file };
        context.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>(),
            files);

        return context.Request;
    }

    private sealed class ApiIntegrationTestFactory(
        IDocumentClassifier? classifier = null,
        IReceiptExtractor? receiptExtractor = null,
        IShoppingListExtractor? shoppingListExtractor = null)
        : WebApplicationFactory<Program>
    {
        private const string TestApiKeyEnvironmentVariable = "MAF_DOCUMENT_PROCESSOR_TEST_API_KEY";

        public ApiIntegrationTestFactory()
            : this(null, null, null)
        {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable(TestApiKeyEnvironmentVariable, "test-key");

            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                var modelSettings = CreateTestModelSettings();

                services.RemoveAll<AiModelSettings>();
                services.RemoveAll<IDocumentClassifier>();
                services.RemoveAll<IReceiptExtractor>();
                services.RemoveAll<IShoppingListExtractor>();
                services.RemoveAll<IModelImagePreprocessor>();

                services.AddSingleton(modelSettings);
                services.AddSingleton<IModelImagePreprocessor, PassThroughImagePreprocessor>();
                services.AddScoped(_ => classifier ?? new FakeDocumentClassifier());
                services.AddScoped(_ => receiptExtractor ?? new FakeReceiptExtractor());
                services.AddScoped(_ => shoppingListExtractor ?? new FakeShoppingListExtractor());
            });
        }

        public static AiModelSettings CreateTestModelSettings()
        {
            var defaults = AiModelSettingsDefaults.CreateTogetherDefaults();
            return new AiModelSettings(
                defaults.DocumentClassification with
                {
                    ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable
                },
                defaults.DocumentExtraction with
                {
                    ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable
                },
                defaults.TextTesting with
                {
                    ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable
                });
        }
    }

    private sealed class FakeDocumentClassifier : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(
                    DocumentCategory.Receipt,
                    0.91m,
                    "receipt layout",
                    "receipt"),
                new ModelTokenUsage("classification", "test-classifier", 1, 2, 3)));
        }
    }

    private sealed class FakeReceiptExtractor : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData(
                    "Meadow Vale Supermarket",
                    21.02m,
                    new DateOnly(2024, 5, 28),
                    "Visa",
                    "GBP"),
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12)));
        }
    }

    private sealed class FakeShoppingListExtractor : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                new ShoppingListData(
                    "Weekly groceries",
                    [new ShoppingListItem("milk", 2, "pints", false)],
                    Notes: null),
                new ModelTokenUsage("shopping_list_extraction", "test-shopping-list-extractor", 4, 8, 12)));
        }
    }

    private sealed class ThrowingReceiptExtractor(Exception exception) : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw exception;
        }
    }

    private sealed class CancellationObservingReceiptExtractor : IReceiptExtractor
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ObservedCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation.TrySetResult(cancellationToken.IsCancellationRequested);
                throw;
            }

            throw new InvalidOperationException("The cancellation test should cancel before extraction completes.");
        }
    }

    private sealed class PassThroughImagePreprocessor : IModelImagePreprocessor
    {
        public ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
            FileRequest request,
            ModelImagePreprocessingPurpose purpose,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelImagePreprocessingResult(
                request,
                purpose,
                WasResized: false,
                OriginalWidth: 1,
                OriginalHeight: 1,
                Width: 1,
                Height: 1,
                request.FileSizeBytes,
                request.FileSizeBytes));
        }
    }
}

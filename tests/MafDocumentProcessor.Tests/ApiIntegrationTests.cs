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
        using var content = CreateMultipartImageContent(
            "receipt.png",
            "image/png",
            [1, 2, 3],
            sourceId: "receipt-contract-test");

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal(DocumentCategory.Receipt, body.Category);
        Assert.Equal("receipt.png", body.Metadata.FileName);
        Assert.Equal("receipt-contract-test", body.Metadata.SourceId);
        Assert.Equal(0.91m, body.Classification.Confidence);
        Assert.Equal(HumanReviewStatus.NotRequired, body.HumanReview.Status);
        Assert.NotNull(body.Document);
        Assert.Equal(DocumentCategory.Receipt, body.Document.Category);
        Assert.Equal("receipt.png", body.Document.Metadata.FileName);
        var data = Assert.IsType<JsonElement>(body.Document?.Data);
        Assert.Equal("Meadow Vale Supermarket", data.Deserialize<ReceiptData>(JsonOptions)?.StoreName);
        Assert.Equal(PolicyDecision.Approved, body.Document.PolicyResult?.Decision);
        Assert.True(body.Document.Validation.IsValid);
        Assert.Equal(2, body.ModelUsage.Calls.Count);
        Assert.Equal(15, body.ModelUsage.TotalTokens);
        Assert.Empty(body.Errors);
        Assert.Empty(body.Warnings);
    }

    [Fact]
    public async Task ProcessDocument_WithShoppingListImage_ReturnsMappedResponse()
    {
        using var factory = new ApiIntegrationTestFactory(
            classifier: new FakeDocumentClassifier(DocumentCategory.ShoppingList));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("shopping-list.jpg", "image/jpeg", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal(DocumentCategory.ShoppingList, body.Category);
        Assert.Equal(HumanReviewStatus.NotRequired, body.HumanReview.Status);
        Assert.NotNull(body.Document);
        Assert.Equal(DocumentCategory.ShoppingList, body.Document.Category);
        Assert.Null(body.Document.PolicyResult);
        Assert.True(body.Document.Validation.IsValid);
        var data = Assert.IsType<JsonElement>(body.Document.Data);
        var shoppingList = data.Deserialize<ShoppingListData>(JsonOptions);
        Assert.NotNull(shoppingList);
        Assert.Equal("Weekly groceries", shoppingList.Title);
        Assert.Equal("milk", Assert.Single(shoppingList.Items).Name);
        Assert.Empty(body.Errors);
        Assert.Empty(body.Warnings);
    }

    [Fact]
    public async Task ProcessDocument_WithSujikoImage_ReturnsMappedResponse()
    {
        using var factory = new ApiIntegrationTestFactory(
            classifier: new FakeDocumentClassifier(DocumentCategory.SujikoPuzzle));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("sujiko.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal(DocumentCategory.SujikoPuzzle, body.Category);
        Assert.Equal(HumanReviewStatus.NotRequired, body.HumanReview.Status);
        Assert.NotNull(body.Document);
        Assert.Equal(DocumentCategory.SujikoPuzzle, body.Document.Category);
        Assert.Null(body.Document.PolicyResult);
        Assert.True(body.Document.Validation.IsValid);
        var data = Assert.IsType<JsonElement>(body.Document.Data);
        var puzzle = data.Deserialize<SujikoPuzzleData>(JsonOptions);
        Assert.NotNull(puzzle);
        Assert.Equal(21, puzzle.QuadrantTotals.TopLeft);
        Assert.Equal(new SujikoCellValue(2, 2, 1), Assert.Single(puzzle.GivenCells));
        Assert.Empty(body.Errors);
        Assert.Empty(body.Warnings);
    }

    [Fact]
    public async Task ProcessDocument_WithUnsupportedDocument_ReturnsNormalFailureResponse()
    {
        using var factory = new ApiIntegrationTestFactory(
            classifier: new FakeDocumentClassifier(
                DocumentCategory.Unknown,
                documentTypeDescription: "car registration document"));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("document.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body.IsSuccess);
        Assert.Equal(DocumentCategory.Unknown, body.Category);
        Assert.Equal(HumanReviewStatus.Required, body.HumanReview.Status);
        Assert.Null(body.Document);
        Assert.Single(body.ModelUsage.Calls);
        Assert.Contains(
            "This appears to be a car registration document",
            Assert.Single(body.Errors),
            StringComparison.Ordinal);
        Assert.Empty(body.Warnings);
    }

    [Fact]
    public async Task ProcessDocument_WithoutMultipartForm_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("not a multipart request");

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "Expected a multipart form request",
            "form");
    }

    [Fact]
    public async Task ProcessDocument_WithoutImageField_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("missing-image"), "sourceId");

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "uploaded image file is required",
            "image");
    }

    [Fact]
    public async Task ProcessDocument_WithEmptyImage_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("empty.png", "image/png", []);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "uploaded image file is required",
            "image");
    }

    [Fact]
    public async Task ProcessDocument_WithUnsupportedContentType_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.txt", "text/plain", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "Unsupported content type",
            "image");
    }

    [Fact]
    public async Task ProcessDocument_WithUnsupportedExtension_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory();
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.gif", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "Unsupported file extension",
            "image");
    }

    [Fact]
    public async Task ProcessDocument_WithOversizedImage_ReturnsErrorContract()
    {
        using var factory = new ApiIntegrationTestFactory(
            intakeSettings: new DocumentIntakeSettings { MaxUploadBytes = 2 });
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_document_upload",
            "2 bytes or smaller",
            "image");
    }

    [Fact]
    public async Task ProcessDocument_WhenModelConfigurationIsMissing_ReturnsInternalServerErrorContract()
    {
        var missingEnvironmentVariable = $"MAF_MISSING_TEST_KEY_{Guid.NewGuid():N}";
        using var factory = new ApiIntegrationTestFactory(
            modelSettings: ApiIntegrationTestFactory.CreateTestModelSettings(missingEnvironmentVariable));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.InternalServerError,
            "model_configuration_invalid",
            missingEnvironmentVariable,
            expectedTarget: null);
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

        await AssertErrorContractAsync(
            response,
            HttpStatusCode.BadGateway,
            "model_response_invalid",
            "invalid JSON",
            expectedTarget: null);
    }

    [Fact]
    public async Task ProcessDocument_WhenModelTimesOut_ReturnsGatewayTimeoutContract()
    {
        await AssertProcessingExceptionContractAsync(
            new TimeoutException("provider timeout"),
            HttpStatusCode.GatewayTimeout,
            "model_timeout",
            "did not return a response before the configured timeout");
    }

    [Fact]
    public async Task ProcessDocument_WhenModelProviderFails_ReturnsBadGatewayContract()
    {
        await AssertProcessingExceptionContractAsync(
            new ModelProviderException(
                "The configured provider rejected the request.",
                new HttpRequestException("provider unavailable")),
            HttpStatusCode.BadGateway,
            "model_provider_failed",
            "provider rejected the request");
    }

    [Fact]
    public async Task ProcessDocument_WhenKnownProcessingFails_ReturnsBadGatewayContract()
    {
        await AssertProcessingExceptionContractAsync(
            new InvalidOperationException("The workflow produced no result."),
            HttpStatusCode.BadGateway,
            "document_processing_failed",
            "workflow produced no result");
    }

    [Fact]
    public async Task ProcessDocument_WhenUnexpectedFailureEscapes_ReturnsInternalServerErrorContract()
    {
        await AssertProcessingExceptionContractAsync(
            new FormatException("unexpected test failure"),
            HttpStatusCode.InternalServerError,
            "document_processing_unhandled",
            "Error type: FormatException");
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
        var modelSettings = ApiIntegrationTestFactory.CreateTestModelSettings();
        Environment.SetEnvironmentVariable(
            modelSettings.DocumentClassification.ApiKeyEnvironmentVariable,
            "test-key");
        var request = CreateHttpRequestWithFormFile("receipt.png", "image/png", [1, 2, 3]);

        var requestTask = DocumentProcessingEndpoints.ProcessDocumentAsync(
            request,
            new DocumentIntakeSettings(),
            modelSettings,
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
        byte[] bytes,
        string fieldName = "image",
        string? sourceId = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, fieldName, fileName);
        if (sourceId is not null)
        {
            content.Add(new StringContent(sourceId), "sourceId");
        }

        return content;
    }

    private static async Task AssertProcessingExceptionContractAsync(
        Exception exception,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string expectedMessageFragment)
    {
        using var factory = new ApiIntegrationTestFactory(
            receiptExtractor: new ThrowingReceiptExtractor(exception));
        using var client = factory.CreateClient();
        using var content = CreateMultipartImageContent("receipt.png", "image/png", [1, 2, 3]);

        using var response = await client.PostAsync("/api/documents/process", content);

        await AssertErrorContractAsync(
            response,
            expectedStatus,
            expectedCode,
            expectedMessageFragment,
            expectedTarget: null);
    }

    private static async Task AssertErrorContractAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string expectedMessageFragment,
        string? expectedTarget)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedCode, body.Code);
        Assert.Equal(expectedTarget, body.Target);
        Assert.Contains(expectedMessageFragment, body.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
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
        IShoppingListExtractor? shoppingListExtractor = null,
        ISujikoPuzzleExtractor? sujikoPuzzleExtractor = null,
        AiModelSettings? modelSettings = null,
        DocumentIntakeSettings? intakeSettings = null)
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
                var configuredModelSettings = modelSettings ?? CreateTestModelSettings();
                var configuredIntakeSettings = intakeSettings ?? new DocumentIntakeSettings();

                services.RemoveAll<AiModelSettings>();
                services.RemoveAll<DocumentIntakeSettings>();
                services.RemoveAll<IDocumentClassifier>();
                services.RemoveAll<IReceiptExtractor>();
                services.RemoveAll<IShoppingListExtractor>();
                services.RemoveAll<ISujikoPuzzleExtractor>();
                services.RemoveAll<IModelImagePreprocessor>();

                services.AddSingleton(configuredModelSettings);
                services.AddSingleton(configuredIntakeSettings);
                services.AddSingleton<IModelImagePreprocessor, PassThroughImagePreprocessor>();
                services.AddScoped(_ => classifier ?? new FakeDocumentClassifier());
                services.AddScoped(_ => receiptExtractor ?? new FakeReceiptExtractor());
                services.AddScoped(_ => shoppingListExtractor ?? new FakeShoppingListExtractor());
                services.AddScoped(_ => sujikoPuzzleExtractor ?? new FakeSujikoPuzzleExtractor());
            });
        }

        public static AiModelSettings CreateTestModelSettings(
            string apiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable)
        {
            var defaults = AiModelSettingsDefaults.CreateTogetherDefaults();
            return new AiModelSettings(
                defaults.DocumentClassification with
                {
                    ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable
                },
                defaults.DocumentExtraction with
                {
                    ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable
                },
                defaults.TextTesting with
                {
                    ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable
                });
        }
    }

    private sealed class FakeDocumentClassifier(
        DocumentCategory category = DocumentCategory.Receipt,
        string? documentTypeDescription = "receipt") : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(
                    category,
                    0.91m,
                    "test document layout",
                    documentTypeDescription),
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

    private sealed class FakeSujikoPuzzleExtractor : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                new SujikoPuzzleData(
                    new SujikoQuadrantTotals(21, 12, 21, 17),
                    [new SujikoCellValue(2, 2, 1)]),
                new ModelTokenUsage("sujiko_puzzle_extraction", "test-sujiko-extractor", 4, 8, 12)));
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

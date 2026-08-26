using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Api.Endpoints;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class ApiCaptureIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ProcessCapture_WithOneReceiptImage_ReturnsCaptureAggregate()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = CreateCaptureContent(("receipt.png", CreatePng(80, 80)));
        content.Add(new StringContent("desk-1"), "sourceId");

        using var response = await client.PostAsync("/api/document-captures/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompositeCaptureProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(CaptureProcessingStatus.Succeeded, body.Status);
        Assert.Equal("desk-1", body.Metadata.SourceId);
        Assert.Equal(1, body.Metadata.SourceCount);
        var source = Assert.Single(body.Sources);
        Assert.Equal("source-001", source.SourceItemId);
        var member = Assert.Single(body.Members);
        Assert.Equal(CaptureMemberDisposition.Accepted, member.Disposition);
        Assert.Equal(DocumentCategory.Receipt, member.Result?.Category);
        Assert.True(member.Result?.IsSuccess);
        Assert.Null(member.Error);
        Assert.Contains(body.ModelUsage.Calls, call => call.Operation == ModelDocumentRegionDetector.Operation);
        Assert.Contains(body.ModelUsage.Calls, call => call.Operation == "classification");
        Assert.Contains(body.ModelUsage.Calls, call => call.Operation == "receipt_extraction");
    }

    [Fact]
    public async Task ProcessCapture_WithMultipleImages_PreservesSourceOrder()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = CreateCaptureContent(
            ("first.png", CreatePng(80, 80)),
            ("second.png", CreatePng(64, 64)));

        using var response = await client.PostAsync("/api/document-captures/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompositeCaptureProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(2, body.Sources.Count);
        Assert.Equal("first.png", body.Sources[0].Metadata.FileName);
        Assert.Equal("second.png", body.Sources[1].Metadata.FileName);
        Assert.Equal(2, body.Members.Count);
        Assert.Equal("source-001-document-001", body.Members[0].MemberId);
        Assert.Equal("source-002-document-001", body.Members[1].MemberId);
    }

    [Fact]
    public async Task ProcessCapture_WithInvalidSibling_ReturnsPartialSuccess()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = CreateCaptureContent(
            ("broken.png", [1, 2, 3]),
            ("receipt.png", CreatePng(80, 80)));

        using var response = await client.PostAsync("/api/document-captures/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompositeCaptureProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(CaptureProcessingStatus.PartiallySucceeded, body.Status);
        Assert.Equal(CaptureProcessingStatus.Failed, body.Sources[0].Status);
        Assert.Equal(CaptureProcessingStatus.Succeeded, body.Sources[1].Status);
        Assert.Single(body.Members, member => member.Disposition == CaptureMemberDisposition.Accepted);
    }

    [Fact]
    public async Task ProcessCapture_WithoutImages_ReturnsErrorContract()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("missing"), "sourceId");

        using var response = await client.PostAsync("/api/document-captures/process", content);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_document_upload", "images");
    }

    [Fact]
    public async Task ProcessCapture_WithoutMultipart_ReturnsErrorContract()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("not multipart");

        using var response = await client.PostAsync("/api/document-captures/process", content);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_document_upload", "form");
    }

    [Fact]
    public async Task ProcessCapture_WithTooManyImages_ReturnsErrorContract()
    {
        using var factory = new CaptureApiFactory(
            captureOptions: new CompositeCaptureOptions(MaxSourceCount: 1, MaxConcurrentSources: 1, MaxConcurrentMembers: 1));
        using var client = factory.CreateClient();
        using var content = CreateCaptureContent(
            ("one.png", CreatePng(32, 32)),
            ("two.png", CreatePng(32, 32)));

        using var response = await client.PostAsync("/api/document-captures/process", content);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_document_upload", "images");
    }

    [Fact]
    public async Task ProcessCapture_WhenCanceled_PropagatesCancellation()
    {
        var detector = new CancellationObservingRegionDetector();
        using var factory = new CaptureApiFactory(regionDetector: detector);
        using var client = factory.CreateClient();
        using var content = CreateCaptureContent(("receipt.png", CreatePng(80, 80)));
        using var cancellation = new CancellationTokenSource();

        var requestTask = client.PostAsync("/api/document-captures/process", content, cancellation.Token);
        await detector.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Assert.True(await detector.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ProcessDocument_StillUsesTheIndividualContract()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(CreatePng(40, 40));
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "image", "receipt.png");

        using var response = await client.PostAsync("/api/documents/process", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentProcessingResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.IsSuccess);
        Assert.Equal(2, body.ModelUsage.Calls.Count);
        Assert.DoesNotContain(
            body.ModelUsage.Calls,
            call => call.Operation == ModelDocumentRegionDetector.Operation);
    }

    [Fact]
    public async Task OpenApi_DocumentsTheCaptureEndpoint()
    {
        using var factory = new CaptureApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/document-captures/process", document, StringComparison.Ordinal);
        Assert.Contains("/api/documents/process", document, StringComparison.Ordinal);
        Assert.Contains("ProcessDocumentCapture", document, StringComparison.Ordinal);
    }

    private static MultipartFormDataContent CreateCaptureContent(
        params (string FileName, byte[] Bytes)[] images)
    {
        var content = new MultipartFormDataContent();
        foreach (var (fileName, bytes) in images)
        {
            var image = new ByteArrayContent(bytes);
            image.Headers.ContentType = new MediaTypeHeaderValue(
                fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
            content.Add(image, DocumentCaptureEndpoints.ImagesFormFieldName, fileName);
        }

        return content;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string target)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal(code, body.Code);
        Assert.Equal(target, body.Target);
        Assert.False(string.IsNullOrWhiteSpace(body.TraceId));
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private sealed class CaptureApiFactory(
        IDocumentRegionDetector? regionDetector = null,
        CompositeCaptureOptions? captureOptions = null)
        : WebApplicationFactory<Program>
    {
        private const string TestApiKeyEnvironmentVariable = "MAF_DOCUMENT_PROCESSOR_TEST_API_KEY";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable(TestApiKeyEnvironmentVariable, "test-key");
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                var settings = CreateTestModelSettings();
                services.RemoveAll<AiModelSettings>();
                services.RemoveAll<CompositeCaptureOptions>();
                services.RemoveAll<IDocumentClassifier>();
                services.RemoveAll<IReceiptExtractor>();
                services.RemoveAll<IShoppingListExtractor>();
                services.RemoveAll<ISujikoPuzzleExtractor>();
                services.RemoveAll<IDocumentRegionDetector>();
                services.RemoveAll<IModelImagePreprocessor>();

                services.AddSingleton(settings);
                services.AddSingleton(captureOptions ?? new CompositeCaptureOptions(
                    MaxConcurrentSources: 2,
                    MaxConcurrentMembers: 2,
                    RegionEdgePadding: 0));
                services.AddSingleton<IModelImagePreprocessor, PassThroughPreprocessor>();
                services.AddScoped<IDocumentClassifier>(_ => new ReceiptClassifier());
                services.AddScoped<IReceiptExtractor>(_ => new ReceiptExtractor());
                services.AddScoped<IShoppingListExtractor>(_ => new UnusedShoppingListExtractor());
                services.AddScoped<ISujikoPuzzleExtractor>(_ => new UnusedSujikoExtractor());
                services.AddScoped(_ => regionDetector ?? new SingleRegionDetector());
            });
        }

        private static AiModelSettings CreateTestModelSettings()
        {
            var defaults = AiModelSettingsDefaults.CreateTogetherDefaults();
            return new AiModelSettings(
                defaults.DocumentClassification with { ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable },
                defaults.DocumentExtraction with { ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable },
                defaults.TextTesting with { ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable },
                defaults.DocumentRegionDetection with { ApiKeyEnvironmentVariable = TestApiKeyEnvironmentVariable });
        }
    }

    private sealed class SingleRegionDetector : IDocumentRegionDetector
    {
        public ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DocumentRegionProposal> proposals =
            [
                new DocumentRegionProposal(
                    source.Source.SourceItemId,
                    1,
                    new ProposedNormalizedBounds(0.1, 0.1, 0.6, 0.6),
                    outline: null,
                    confidence: 0.95m)
            ];
            return ValueTask.FromResult(new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                proposals,
                new ModelTokenUsage(ModelDocumentRegionDetector.Operation, "test-detector", 4, 2, 6)));
        }
    }

    private sealed class CancellationObservingRegionDetector : IDocumentRegionDetector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ObservedCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
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

            throw new InvalidOperationException("The cancellation test should cancel before detection completes.");
        }
    }

    private sealed class ReceiptClassifier : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(DocumentCategory.Receipt, 0.91m, "test layout"),
                new ModelTokenUsage("classification", "test-classifier", 1, 2, 3)));
        }
    }

    private sealed class ReceiptExtractor : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData("Meadow Vale Supermarket", 21.02m, new DateOnly(2024, 5, 28), "Visa", "GBP"),
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12)));
        }
    }

    private sealed class UnusedShoppingListExtractor : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Shopping-list extraction should not run.");
        }
    }

    private sealed class UnusedSujikoExtractor : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Sujiko extraction should not run.");
        }
    }

    private sealed class PassThroughPreprocessor : IModelImagePreprocessor
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

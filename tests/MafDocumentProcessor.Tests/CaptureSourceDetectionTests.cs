using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace MafDocumentProcessor.Tests;

public sealed class CaptureSourceDetectionTests
{
    [Fact]
    public void Decoder_AppliesExifOrientationAndRetainsTheHighResolutionSource()
    {
        var decoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions());
        var source = CreateSource(
            CreateJpeg(40, 20, ExifOrientationMode.RightTop),
            "rotated.jpg",
            "image/jpeg");

        using var decoded = decoder.Decode(source);

        Assert.Equal(40, decoded.OriginalWidthPixels);
        Assert.Equal(20, decoded.OriginalHeightPixels);
        Assert.Equal(20, decoded.WidthPixels);
        Assert.Equal(40, decoded.HeightPixels);
        Assert.Same(source, decoded.Source);
    }

    [Fact]
    public async Task DetectionImage_IsPreparedFromTheOrientedImageWithoutAnotherDecode()
    {
        var decoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions());
        using var decoded = decoder.Decode(CreateSource(
            CreateJpeg(400, 200, ExifOrientationMode.RightTop),
            "desk.jpg",
            "image/jpeg"));
        var preparer = new CaptureDetectionImagePreparer(new ModelImagePreprocessingSettings(
            RegionDetectionMaxLongEdgePixels: 100));

        var request = await preparer.PrepareAsync(decoded, CancellationToken.None);
        var imageInfo = Image.Identify(request.Content);

        Assert.NotNull(imageInfo);
        Assert.Equal(50, imageInfo.Width);
        Assert.Equal(100, imageInfo.Height);
        Assert.Equal("desk.model-region-detection.jpg", request.FileName);
        Assert.Equal("image/jpeg", request.ContentType);
        Assert.Equal(request.Content.LongLength, request.FileSizeBytes);
    }

    [Fact]
    public void Decoder_RejectsUnsupportedOrOversizedSourcesBeforeDetection()
    {
        var decoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions());
        var dimensionLimitedDecoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions(
            MaxSourceWidthPixels: 20,
            MaxSourceHeightPixels: 20,
            MaxSourcePixelCount: 400));

        Assert.Throws<CaptureSourceValidationException>(() => decoder.Decode(
            CreateSource([1, 2, 3], "not-an-image.png", "image/png")));
        Assert.Throws<CaptureSourceValidationException>(() => dimensionLimitedDecoder.Decode(
            CreateSource(CreateJpeg(30, 10), "wide.jpg", "image/jpeg")));
        Assert.Throws<CaptureSourceValidationException>(() => decoder.Decode(
            CreateSource(CreateJpeg(10, 10), "image.gif", "image/gif")));
        Assert.Throws<CaptureSourceValidationException>(() => decoder.Decode(
            CreateSource(CreateJpeg(10, 10), "image.png", "image/png")));
    }

    [Fact]
    public void Parser_ReturnsTypedProposalsAndLeavesGeometryForDeterministicValidation()
    {
        var proposals = DocumentRegionResponseParser.Parse(
            """
            {
              "regions": [
                {
                  "bounds": { "x": -0.02, "y": 0.1, "width": 0.5, "height": 0.7 },
                  "outline": [
                    { "x": 0.0, "y": 0.1 },
                    { "x": 0.5, "y": 0.1 },
                    { "x": 0.5, "y": 0.8 },
                    { "x": 0.0, "y": 0.8 }
                  ],
                  "confidence": 0.94
                }
              ]
            }
            """,
            "source-003");

        var proposal = Assert.Single(proposals);
        Assert.Equal("source-003", proposal.SourceItemId);
        Assert.Equal(1, proposal.DetectionIndex);
        Assert.Equal(-0.02, proposal.Bounds.X);
        Assert.Equal(4, proposal.Outline?.Count);
        Assert.Equal(0.94m, proposal.Confidence);
    }

    [Fact]
    public void Parser_AcceptsAnEmptyDetection()
    {
        var proposals = DocumentRegionResponseParser.Parse(
            "```json\n{\"regions\":[]}\n```",
            "source-001");

        Assert.Empty(proposals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"regions\":[{\"confidence\":0.8}]}")]
    [InlineData("{\"regions\":[{\"bounds\":{\"x\":0,\"y\":0,\"width\":1}}]}")]
    public void Parser_RejectsMalformedModelResponses(string? content)
    {
        Assert.Throws<DocumentModelResponseException>(() =>
            DocumentRegionResponseParser.Parse(content, "source-001"));
    }

    [Fact]
    public async Task ModelDetector_MakesOneConfiguredCallAndReturnsItsUsage()
    {
        var chatClient = new RecordingChatClient("""
            {"regions":[{"bounds":{"x":0.1,"y":0.2,"width":0.3,"height":0.4},"confidence":0.91}]}
            """);
        var role = AiModelSettingsDefaults.CreateTogetherQwen35NineBRole("capture-test-detection");
        var detector = new ModelDocumentRegionDetector(
            chatClient,
            new PassThroughDetectionImagePreparer(),
            role,
            new CompositeCaptureOptions());
        var decoder = new CaptureSourceImageDecoder(new CompositeCaptureOptions());
        using var source = decoder.Decode(CreateSource(CreateJpeg(40, 20), "desk.jpg", "image/jpeg"));

        var result = await detector.DetectAsync(source, CancellationToken.None);

        var request = Assert.Single(chatClient.Requests);
        Assert.Equal(ModelDocumentRegionDetector.Operation, request.Operation);
        Assert.Same(role, request.Settings);
        Assert.Contains(
            request.Messages.SelectMany(message => message.Content),
            content => content is ModelImageContent);
        Assert.Single(result.Value);
        Assert.Equal("capture-detector", result.Usage.ModelId);
    }

    [Fact]
    public async Task InvalidSource_IsIsolatedWithoutCallingTheDetector()
    {
        var detector = new StubRegionDetector();
        var service = CreateDetectionService(detector);
        var input = CreateInput(CreateSource([1, 2, 3], "broken.png", "image/png"));

        using var output = await service.DetectAsync(input, CancellationToken.None);

        Assert.False(output.IsSuccess);
        Assert.Null(output.OrientedSource);
        Assert.Empty(output.Proposals);
        Assert.Equal("invalid_capture_source", Assert.Single(output.Errors).Code);
        Assert.Empty(output.ModelUsage.Calls);
        Assert.Equal(0, detector.CallCount);
        Assert.Equal(input.Source.SourceItemId, output.Context.SourceItemId);
    }

    [Fact]
    public async Task InvalidModelResponse_IsIsolatedAndRetainsKnownUsage()
    {
        var usage = CreateUsage();
        var service = CreateDetectionService(new ThrowingRegionDetector(
            new DocumentRegionModelResponseException(
                "Detector JSON was invalid.",
                usage,
                new DocumentModelResponseException("Invalid JSON."))));
        var input = CreateInput(CreateSource(CreateJpeg(40, 20), "desk.jpg", "image/jpeg"));

        using var output = await service.DetectAsync(input, CancellationToken.None);

        Assert.False(output.IsSuccess);
        Assert.Equal("model_response_invalid", Assert.Single(output.Errors).Code);
        Assert.Same(usage, Assert.Single(output.ModelUsage.Calls));
        Assert.Null(output.OrientedSource);
        Assert.NotNull(output.ImageMetadata);
    }

    [Theory]
    [MemberData(nameof(IsolatedDetectorFailures))]
    public async Task ProviderAndTimeoutFailures_AreIsolated(Exception exception, string expectedCode)
    {
        var service = CreateDetectionService(new ThrowingRegionDetector(exception));
        var input = CreateInput(CreateSource(CreateJpeg(40, 20), "desk.jpg", "image/jpeg"));

        using var output = await service.DetectAsync(input, CancellationToken.None);

        Assert.False(output.IsSuccess);
        Assert.Equal(expectedCode, Assert.Single(output.Errors).Code);
        Assert.Null(output.OrientedSource);
    }

    public static TheoryData<Exception, string> IsolatedDetectorFailures => new()
    {
        { new TimeoutException("test timeout"), "model_timeout" },
        {
            new ModelProviderException("provider failed", new HttpRequestException("offline")),
            "model_provider_failed"
        }
    };

    [Fact]
    public async Task MissingModelConfiguration_RemainsACaptureLevelFailure()
    {
        var expected = new ModelConfigurationException("Detection role is not configured.");
        var service = CreateDetectionService(new ThrowingRegionDetector(expected));
        var input = CreateInput(CreateSource(CreateJpeg(40, 20), "desk.jpg", "image/jpeg"));

        var actual = await Assert.ThrowsAsync<ModelConfigurationException>(async () =>
            await service.DetectAsync(input, CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task DetectionExecutor_EmitsCorrelatedEventsAndAReusableOrientedSource()
    {
        var detector = new StubRegionDetector();
        var executor = new CaptureSourceDetectionExecutor(CreateDetectionService(detector));
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
        var input = CreateInput(CreateSource(
            CreateJpeg(40, 20, ExifOrientationMode.RightTop),
            "desk.jpg",
            "image/jpeg"));

        var run = await InProcessExecution.RunAsync(workflow, input);
        var events = run.NewEvents.ToArray();
        using var output = GetOutput(events);

        Assert.True(output.IsSuccess);
        Assert.Equal(1, detector.CallCount);
        Assert.NotNull(output.OrientedSource);
        Assert.Single(output.Proposals);
        var decoded = Assert.Single(events, evt => evt.Data is CaptureSourceDecodedEvent);
        var decodedData = Assert.IsType<CaptureSourceDecodedEvent>(decoded.Data);
        Assert.Equal("trace-44", decodedData.TraceId);
        Assert.Equal("capture-44", decodedData.CaptureId);
        Assert.Equal(20, decodedData.OrientedWidthPixels);
        Assert.Equal(40, decodedData.OrientedHeightPixels);
        var completed = Assert.Single(
            events,
            evt => evt.Data is CaptureSourceDetectionCompletedEvent);
        var completedData = Assert.IsType<CaptureSourceDetectionCompletedEvent>(completed.Data);
        Assert.True(completedData.IsSuccess);
        Assert.Equal(1, completedData.ProposalCount);
        Assert.Equal("capture-detector", completedData.ModelId);
        Assert.Empty(completedData.ErrorCodes);
    }

    [Fact]
    public async Task DetectionExecutor_ReturnsInvalidSourceAsNormalOutputAndEvent()
    {
        var detector = new StubRegionDetector();
        var executor = new CaptureSourceDetectionExecutor(CreateDetectionService(detector));
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
        var input = CreateInput(CreateSource([1, 2, 3], "broken.png", "image/png"));

        var run = await InProcessExecution.RunAsync(workflow, input);
        var events = run.NewEvents.ToArray();
        using var output = GetOutput(events);

        Assert.Empty(events.OfType<WorkflowErrorEvent>());
        Assert.False(output.IsSuccess);
        Assert.Equal(0, detector.CallCount);
        Assert.DoesNotContain(events, evt => evt.Data is CaptureSourceDecodedEvent);
        var completed = Assert.Single(
            events,
            evt => evt.Data is CaptureSourceDetectionCompletedEvent);
        var completedData = Assert.IsType<CaptureSourceDetectionCompletedEvent>(completed.Data);
        Assert.False(completedData.IsSuccess);
        Assert.Equal(["invalid_capture_source"], completedData.ErrorCodes);
    }

    [Fact]
    public async Task DetectionService_PropagatesRequestCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var detector = new CancellationObservingRegionDetector();
        var service = CreateDetectionService(detector);
        var input = CreateInput(CreateSource(CreateJpeg(40, 20), "desk.jpg", "image/jpeg"));

        var task = service.DetectAsync(input, cancellation.Token).AsTask();
        await detector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.True(await detector.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static CaptureSourceDetectionService CreateDetectionService(IDocumentRegionDetector detector)
    {
        return new CaptureSourceDetectionService(
            new CaptureSourceImageDecoder(new CompositeCaptureOptions()),
            detector);
    }

    private static CaptureSourceDetectionInput CreateInput(CompositeCaptureSource source)
    {
        return new CaptureSourceDetectionInput(
            new CaptureWorkflowContext("trace-44", "capture-44", "claim-44"),
            source);
    }

    private static CompositeCaptureSource CreateSource(
        byte[] content,
        string fileName,
        string contentType)
    {
        var request = new FileRequest(
            content,
            fileName,
            contentType,
            content.LongLength,
            DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
            "claim-44");
        return new CompositeCaptureSource("source-001", 1, request);
    }

    private static byte[] CreateJpeg(
        int width,
        int height,
        ushort? orientation = null)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        if (orientation.HasValue)
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation.Value);
        }

        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 90 });
        return output.ToArray();
    }

    private static ModelTokenUsage CreateUsage()
    {
        return new ModelTokenUsage(
            ModelDocumentRegionDetector.Operation,
            "capture-detector",
            InputTokens: 10,
            OutputTokens: 5,
            TotalTokens: 15);
    }

    private static CaptureSourceDetectionOutput GetOutput(IEnumerable<WorkflowEvent> events)
    {
        var outputEvent = Assert.Single(events.OfType<WorkflowOutputEvent>());
        return Assert.IsType<CaptureSourceDetectionOutput>(outputEvent.Data);
    }

    private sealed class StubRegionDetector : IDocumentRegionDetector
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            IReadOnlyList<DocumentRegionProposal> proposals =
            [
                new DocumentRegionProposal(
                    source.Source.SourceItemId,
                    1,
                    new ProposedNormalizedBounds(0.1, 0.1, 0.5, 0.7),
                    outline: null,
                    confidence: 0.95m)
            ];
            return ValueTask.FromResult(new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                proposals,
                CreateUsage()));
        }
    }

    private sealed class ThrowingRegionDetector(Exception exception) : IDocumentRegionDetector
    {
        public ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    private sealed class CancellationObservingRegionDetector : IDocumentRegionDetector
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ObservedCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation.TrySetResult(cancellationToken.IsCancellationRequested);
                throw;
            }

            throw new InvalidOperationException("The cancellation test should not reach a result.");
        }
    }

    private sealed class PassThroughDetectionImagePreparer : ICaptureDetectionImagePreparer
    {
        public ValueTask<FileRequest> PrepareAsync(
            OrientedCaptureSourceImage source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(source.Source.Request);
        }
    }

    private sealed class RecordingChatClient(string responseContent) : IModelChatClient
    {
        public List<ModelChatRequest> Requests { get; } = [];

        public ValueTask<ModelChatResponse> CompleteAsync(
            ModelChatRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(new ModelChatResponse(responseContent, CreateUsage()));
        }
    }
}

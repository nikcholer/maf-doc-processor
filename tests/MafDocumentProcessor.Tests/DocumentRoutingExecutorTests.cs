using System.Reflection;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Tests;

public sealed class DocumentRoutingExecutorTests
{
    [Theory]
    [InlineData(DocumentCategory.Receipt)]
    [InlineData(DocumentCategory.ShoppingList)]
    [InlineData(DocumentCategory.SujikoPuzzle)]
    public async Task ClassificationExecutor_PreparesSupportedDocumentForExtraction(
        DocumentCategory category)
    {
        var preprocessor = new TrackingImagePreprocessor();
        var classifier = new StubDocumentClassifier(category, confidence: 0.93m);
        var executor = new DocumentClassificationExecutor(classifier, preprocessor);
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
        var originalRequest = CreateRequest();

        var run = await InProcessExecution.RunAsync(workflow, originalRequest);
        var classified = GetOutput<ClassifiedDocument>(run.NewEvents);

        Assert.Equal(category, classified.Classification.Category);
        Assert.Equal(0.93m, classified.Classification.Confidence);
        Assert.Equal("routing-test-classifier", classified.ClassificationUsage.ModelId);
        Assert.Equal("document.extraction.png", classified.Request.FileName);
        Assert.Same(originalRequest, classified.OriginalRequest);
        Assert.Equal(originalRequest.FileName, classified.Metadata.FileName);
        Assert.Equal(originalRequest.ContentType, classified.Metadata.ContentType);
        Assert.Equal(originalRequest.FileSizeBytes, classified.Metadata.FileSizeBytes);
        Assert.Equal(originalRequest.ReceivedAt, classified.Metadata.ReceivedAt);
        Assert.Equal(originalRequest.SourceId, classified.Metadata.SourceId);
        Assert.Equal("routing-test-classifier", classified.Metadata.ModelId);
        Assert.Equal(0.93m, classified.Metadata.ClassificationConfidence);
        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(
            [ModelImagePreprocessingPurpose.Classification, ModelImagePreprocessingPurpose.Extraction],
            preprocessor.Purposes);
        Assert.Equal("document.classification.png", classifier.Requests.Single().FileName);
    }

    [Theory]
    [InlineData(DocumentCategory.Invoice, null, "This appears to be an invoice.")]
    [InlineData(DocumentCategory.Unknown, "event ticket", "This appears to be an event ticket.")]
    public async Task UnsupportedExecutor_PreservesCurrentFailureAndReviewSemantics(
        DocumentCategory category,
        string? description,
        string expectedMessageStart)
    {
        var preprocessor = new TrackingImagePreprocessor();
        var classification = new DocumentClassificationExecutor(
            new StubDocumentClassifier(category, 0.91m, description),
            preprocessor);
        var unsupported = new UnsupportedDocumentResultExecutor();
        var workflow = new WorkflowBuilder(classification)
            .AddEdge(classification, unsupported)
            .WithOutputFrom(unsupported)
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, CreateRequest());
        var result = GetOutput<DocumentProcessingResult>(run.NewEvents);

        Assert.False(result.IsSuccess);
        Assert.Equal(category, result.Category);
        Assert.False(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Null(result.Receipt);
        Assert.Null(result.ShoppingList);
        Assert.Null(result.SujikoPuzzle);
        Assert.StartsWith(expectedMessageStart, Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Equal(result.Errors, result.Validation.Reasons);
        Assert.Equal(result.Errors, result.HumanReview.Reasons);
        Assert.Empty(result.Warnings);
        Assert.Equal("document.png", result.Metadata.FileName);
        Assert.Equal("routing-source", result.Metadata.SourceId);
        Assert.Equal(
            ["classification"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        Assert.Equal([ModelImagePreprocessingPurpose.Classification], preprocessor.Purposes);
    }

    [Fact]
    public async Task ClassificationExecutor_ReportsClassifierFailureWithoutPreparingExtraction()
    {
        var preprocessor = new TrackingImagePreprocessor();
        var expected = new DocumentModelResponseException("Classification returned invalid JSON.");
        var executor = new DocumentClassificationExecutor(
            new ThrowingDocumentClassifier(expected),
            preprocessor);
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, CreateRequest());

        var error = Assert.Single(run.NewEvents.OfType<WorkflowErrorEvent>());
        Assert.True(ContainsException<DocumentModelResponseException>(error.Exception));
        Assert.Equal([ModelImagePreprocessingPurpose.Classification], preprocessor.Purposes);
    }

    [Fact]
    public async Task ClassificationExecutor_PropagatesExecutionCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var preprocessor = new TrackingImagePreprocessor();
        var classifier = new CancellationObservingClassifier();
        var executor = new DocumentClassificationExecutor(
            classifier,
            preprocessor,
            workflowCancellationToken: cancellation.Token);
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();

        var runTask = InProcessExecution.RunAsync(workflow, CreateRequest()).AsTask();
        await classifier.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await cancellation.CancelAsync();
        var run = await runTask;

        var error = Assert.Single(run.NewEvents.OfType<WorkflowErrorEvent>());
        Assert.True(ContainsException<OperationCanceledException>(error.Exception));
        Assert.True(await classifier.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal([ModelImagePreprocessingPurpose.Classification], preprocessor.Purposes);
    }

    private static TOutput GetOutput<TOutput>(IEnumerable<WorkflowEvent> events)
    {
        var eventArray = events.ToArray();
        Assert.Empty(eventArray.OfType<WorkflowErrorEvent>());
        var output = Assert.Single(eventArray.OfType<WorkflowOutputEvent>());
        return Assert.IsType<TOutput>(output.Data);
    }

    private static bool ContainsException<TException>(Exception? exception)
        where TException : Exception
    {
        return exception switch
        {
            null => false,
            TException => true,
            TargetInvocationException { InnerException: { } inner } =>
                ContainsException<TException>(inner),
            AggregateException aggregate =>
                aggregate.InnerExceptions.Any(ContainsException<TException>),
            _ => ContainsException<TException>(exception.InnerException)
        };
    }

    private static FileRequest CreateRequest()
    {
        return new FileRequest(
            [1, 2, 3],
            "document.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2026-08-26T09:30:00Z"),
            SourceId: "routing-source");
    }

    private sealed class TrackingImagePreprocessor : IModelImagePreprocessor
    {
        public List<ModelImagePreprocessingPurpose> Purposes { get; } = [];

        public ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
            FileRequest request,
            ModelImagePreprocessingPurpose purpose,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Purposes.Add(purpose);
            var suffix = purpose.ToString().ToLowerInvariant();
            var preparedRequest = request with { FileName = $"document.{suffix}.png" };
            return ValueTask.FromResult(new ModelImagePreprocessingResult(
                preparedRequest,
                purpose,
                WasResized: false,
                OriginalWidth: 100,
                OriginalHeight: 100,
                Width: 100,
                Height: 100,
                request.FileSizeBytes,
                preparedRequest.FileSizeBytes));
        }
    }

    private sealed class StubDocumentClassifier(
        DocumentCategory category,
        decimal confidence,
        string? description = null) : IDocumentClassifier
    {
        public int CallCount { get; private set; }
        public List<FileRequest> Requests { get; } = [];

        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Requests.Add(request);
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(
                    category,
                    confidence,
                    "Routing executor test classification",
                    description),
                new ModelTokenUsage(
                    "classification",
                    "routing-test-classifier",
                    InputTokens: 10,
                    OutputTokens: 5,
                    TotalTokens: 15)));
        }
    }

    private sealed class ThrowingDocumentClassifier(Exception exception) : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    private sealed class CancellationObservingClassifier : IDocumentClassifier
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ObservedCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
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
}

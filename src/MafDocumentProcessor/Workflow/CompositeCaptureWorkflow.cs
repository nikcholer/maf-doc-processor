using System.Reflection;
using System.Runtime.ExceptionServices;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafDocumentProcessor.Workflow;

public sealed class CompositeCaptureWorkflow(
    ICaptureSourceDetectionService detectionService,
    ICaptureRegionValidationService validationService,
    IDocumentClassifier classifier,
    IReceiptExtractor receiptExtractor,
    IShoppingListExtractor shoppingListExtractor,
    ReceiptPolicyOptions policyOptions,
    CompositeCaptureOptions captureOptions,
    IModelImagePreprocessor? imagePreprocessor = null,
    ISujikoPuzzleExtractor? sujikoPuzzleExtractor = null,
    ILogger<CompositeCaptureWorkflow>? logger = null,
    ILogger<DocumentClassificationExecutor>? classificationLogger = null)
{
    private readonly ILogger<CompositeCaptureWorkflow> _logger =
        logger ?? NullLogger<CompositeCaptureWorkflow>.Instance;

    public async Task<CompositeCaptureResult> RunAsync(
        CompositeCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var documentWorkflow = new DocumentProcessingWorkflow(
            classifier,
            receiptExtractor,
            shoppingListExtractor,
            policyOptions,
            imagePreprocessor,
            sujikoPuzzleExtractor,
            classificationLogger: classificationLogger);
        var sourceWorkflow = CaptureWorkflowFactory.BuildSourceWorkflow(
            detectionService,
            validationService,
            captureOptions,
            cancellationToken);
        var sourceStage = await RunWorkflowAsync<CompositeCaptureRequest, CaptureSourceStageResult>(
            sourceWorkflow,
            request,
            CaptureWorkflowFactory.SourceWorkflowName,
            cancellationToken);
        var memberWorkflow = CaptureWorkflowFactory.BuildMemberWorkflow(
            documentWorkflow,
            captureOptions,
            cancellationToken);
        return await RunWorkflowAsync<CaptureSourceStageResult, CompositeCaptureResult>(
            memberWorkflow,
            sourceStage,
            CaptureWorkflowFactory.MemberWorkflowName,
            cancellationToken);
    }

    private async Task<TResult> RunWorkflowAsync<TInput, TResult>(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        TInput input,
        string workflowName,
        CancellationToken cancellationToken)
        where TInput : notnull
    {
        _logger.LogInformation("Starting MAF workflow {WorkflowName}.", workflowName);
        var run = await InProcessExecution.RunAsync(
            workflow,
            input,
            cancellationToken: cancellationToken);
        var events = run.NewEvents.ToArray();
        _logger.LogInformation(
            "MAF workflow {WorkflowName} emitted {EventCount} events.",
            workflowName,
            events.Length);

        var error = events.OfType<WorkflowErrorEvent>().LastOrDefault();
        if (error is not null)
        {
            var exception = Unwrap(error.Exception)
                ?? new InvalidOperationException("Workflow failed without reporting an exception.");
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var result = events
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<TResult>()
            .LastOrDefault();
        if (result is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"{workflowName} completed without a result.");
        }

        return result;
    }

    private static Exception? Unwrap(Exception? exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocation)
        {
            exception = invocation.InnerException;
        }

        return exception;
    }
}

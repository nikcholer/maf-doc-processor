using System.Reflection;
using System.Runtime.ExceptionServices;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentProcessingWorkflow(
    IDocumentClassifier classifier,
    IReceiptExtractor receiptExtractor,
    IShoppingListExtractor shoppingListExtractor,
    ReceiptPolicyOptions policyOptions,
    IModelImagePreprocessor? imagePreprocessor = null,
    ISujikoPuzzleExtractor? sujikoPuzzleExtractor = null,
    ILogger<DocumentProcessingWorkflow>? logger = null,
    ILogger<DocumentClassificationExecutor>? classificationLogger = null)
{
    private readonly IModelImagePreprocessor _imagePreprocessor =
        imagePreprocessor ?? ModelImagePreprocessor.CreateDefault();
    private readonly ILogger<DocumentProcessingWorkflow> _logger =
        logger ?? NullLogger<DocumentProcessingWorkflow>.Instance;

    public async Task<DocumentProcessingResult> RunAsync(
        FileRequest request,
        CancellationToken cancellationToken = default)
    {
        var workflow = DocumentWorkflowFactory.BuildDocumentRoutingWorkflow(
            classifier,
            receiptExtractor,
            shoppingListExtractor,
            policyOptions,
            _imagePreprocessor,
            sujikoPuzzleExtractor,
            classificationLogger,
            cancellationToken);

        return await RunWorkflowAsync(
            workflow,
            request,
            _logger,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Document processing workflow completed without a result.");
    }

    private static async Task<DocumentProcessingResult?> RunWorkflowAsync(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        FileRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting MAF workflow {WorkflowName} for {FileName}.",
            DocumentWorkflowFactory.DocumentRoutingWorkflowName,
            request.FileName);

        var run = await InProcessExecution.RunAsync(
            workflow,
            request,
            cancellationToken: cancellationToken);

        var events = run.NewEvents.ToArray();
        logger.LogInformation(
            "MAF workflow {WorkflowName} emitted {EventCount} events.",
            DocumentWorkflowFactory.DocumentRoutingWorkflowName,
            events.Length);
        foreach (var evt in events)
        {
            logger.LogDebug(
                "MAF workflow event {WorkflowName}: {EventType}.",
                DocumentWorkflowFactory.DocumentRoutingWorkflowName,
                evt.GetType().Name);
        }

        var error = events
            .OfType<WorkflowErrorEvent>()
            .LastOrDefault();
        if (error is not null)
        {
            var exception = UnwrapWorkflowException(error.Exception)
                ?? new InvalidOperationException("Workflow failed without reporting an exception.");
            logger.LogWarning(
                exception,
                "MAF workflow {WorkflowName} reported an error event.",
                DocumentWorkflowFactory.DocumentRoutingWorkflowName);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var result = events
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<DocumentProcessingResult>()
            .LastOrDefault();
        logger.LogInformation(
            "Completed MAF workflow {WorkflowName}. HasResult={HasResult}.",
            DocumentWorkflowFactory.DocumentRoutingWorkflowName,
            result is not null);

        if (result is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
    }

    private static Exception? UnwrapWorkflowException(Exception? exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocationException)
        {
            exception = invocationException.InnerException;
        }

        return exception;
    }

}

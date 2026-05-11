using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptProcessingWorkflow(
    IDocumentClassifier classifier,
    IReceiptExtractor extractor,
    ReceiptPolicyOptions policyOptions)
{
    public async Task<ReceiptProcessingResult> RunAsync(
        FileRequest request,
        CancellationToken cancellationToken = default)
    {
        var classificationExecutor = new DocumentClassificationExecutor(classifier);
        var extractionExecutor = new ReceiptExtractionExecutor(extractor);
        var validationExecutor = new ReceiptValidationExecutor();
        var policyExecutor = new ReceiptPolicyExecutor(policyOptions);
        var resultExecutor = new ReceiptResultExecutor();

        var workflow = new WorkflowBuilder(classificationExecutor)
            .AddEdge(classificationExecutor, extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, policyExecutor)
            .AddEdge(policyExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName("Receipt Processing")
            .WithDescription("Classifies, extracts, validates, and evaluates a receipt image.")
            .Build();

        var run = await InProcessExecution.RunAsync(
            workflow,
            request,
            cancellationToken: cancellationToken);

        var output = run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<ReceiptProcessingResult>()
            .LastOrDefault();

        return output ?? throw new InvalidOperationException(
            "Receipt workflow completed without a receipt processing result.");
    }
}

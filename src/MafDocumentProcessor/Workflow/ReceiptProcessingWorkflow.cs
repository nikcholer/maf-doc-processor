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
        var extractionExecutor = new ReceiptExtractionExecutor(extractor);
        var validationExecutor = new ReceiptValidationExecutor();
        var policyExecutor = new ReceiptPolicyExecutor(policyOptions);
        var resultExecutor = new ReceiptResultExecutor();

        var classification = await classifier.ClassifyAsync(request, cancellationToken);
        var classifiedDocument = new ClassifiedDocument(
            request,
            DocumentMetadata.FromRequest(
                request,
                classification.Usage.ModelId,
                classification.Value.Confidence),
            classification.Value,
            classification.Usage);

        if (classification.Value.Category != DocumentCategory.Receipt)
        {
            return CreateUnsupportedDocumentResult(classifiedDocument);
        }

        var workflow = new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, policyExecutor)
            .AddEdge(policyExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName("Receipt Processing")
            .WithDescription("Classifies, extracts, validates, and evaluates a receipt image.")
            .Build();

        var run = await InProcessExecution.RunAsync(
            workflow,
            classifiedDocument,
            cancellationToken: cancellationToken);

        var output = run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<ReceiptProcessingResult>()
            .LastOrDefault();

        return output ?? throw new InvalidOperationException(
            "Receipt workflow completed without a receipt processing result. The uploaded document may not have been classified as a receipt.");
    }

    private static ReceiptProcessingResult CreateUnsupportedDocumentResult(ClassifiedDocument document)
    {
        var message = BuildUnsupportedDocumentMessage(document.Classification);

        return new ReceiptProcessingResult(
            document.Classification.Category,
            document.Metadata,
            document.Classification,
            DocumentModelUsage.FromCalls([document.ClassificationUsage]),
            Receipt: null,
            PolicyResult: null,
            ValidationResult.Invalid(message),
            IsSuccess: false,
            Errors: [message],
            Warnings: []);
    }

    private static string BuildUnsupportedDocumentMessage(DocumentClassification classification)
    {
        var description = NormalizeDocumentTypeDescription(classification);
        var article = GetIndefiniteArticle(description);

        return $"This appears to be {article} {description}. This demo can only process receipts right now.";
    }

    private static string NormalizeDocumentTypeDescription(DocumentClassification classification)
    {
        var description = classification.DocumentTypeDescription;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = classification.Category switch
            {
                DocumentCategory.Invoice => "invoice",
                DocumentCategory.Unknown => "unsupported document",
                _ => classification.Category.ToString()
            };
        }

        description = description.Trim().TrimEnd('.');
        foreach (var prefix in new[] { "a ", "an ", "the " })
        {
            if (description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                description = description[prefix.Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = "unsupported document";
        }

        return char.ToLowerInvariant(description[0]) + description[1..];
    }

    private static string GetIndefiniteArticle(string description)
    {
        return description.Length > 0
            && "aeiou".Contains(char.ToLowerInvariant(description[0]))
            ? "an"
            : "a";
    }
}

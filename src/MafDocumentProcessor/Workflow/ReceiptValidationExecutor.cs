using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptValidationExecutor()
    : Executor<ReceiptExtraction, ValidatedReceiptExtraction>("ReceiptValidation")
{
    public override ValueTask<ValidatedReceiptExtraction> HandleAsync(
        ReceiptExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var receipt = message.Receipt;
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(receipt.StoreName))
        {
            reasons.Add("Receipt store name is missing.");
        }

        if (receipt.TotalAmount < 0)
        {
            reasons.Add("Receipt total amount cannot be negative.");
        }

        if (receipt.CurrencyCode is { Length: not 3 })
        {
            reasons.Add("Receipt currency code must be a three-letter ISO-4217 code when present.");
        }

        var validation = reasons.Count == 0
            ? ValidationResult.Valid
            : new ValidationResult(false, reasons);

        return ValueTask.FromResult(new ValidatedReceiptExtraction(message, validation));
    }
}

using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptPolicyExecutor(ReceiptPolicyOptions options)
    : Executor<ValidatedReceiptExtraction, ReceiptPolicyEvaluation>("ReceiptPolicy")
{
    public override ValueTask<ReceiptPolicyEvaluation> HandleAsync(
        ValidatedReceiptExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var receipt = message.Extraction.Receipt;
        var reasons = new List<string>();
        var isWithinReviewThreshold = receipt.TotalAmount <= options.ReviewThreshold;
        var hasPaymentMethod = !string.IsNullOrWhiteSpace(receipt.PaymentMethod);

        if (!isWithinReviewThreshold)
        {
            reasons.Add($"Receipt total {receipt.TotalAmount:0.00} exceeds review threshold {options.ReviewThreshold:0.00}.");
        }

        if (!hasPaymentMethod)
        {
            reasons.Add("Receipt payment method is missing.");
        }

        var decision = isWithinReviewThreshold && hasPaymentMethod
            ? PolicyDecision.Approved
            : PolicyDecision.NeedsReview;

        if (decision == PolicyDecision.Approved)
        {
            reasons.Add("Receipt is within the review threshold and includes a payment method.");
        }

        var allValidationReasons = message.Validation.Reasons.Concat(
            decision == PolicyDecision.Approved ? [] : reasons).ToArray();
        var validation = message.Validation.IsValid && decision == PolicyDecision.Approved
            ? ValidationResult.Valid
            : new ValidationResult(false, allValidationReasons);

        var policy = new ReceiptPolicyResult(
            isWithinReviewThreshold,
            hasPaymentMethod,
            decision,
            reasons);

        return ValueTask.FromResult(new ReceiptPolicyEvaluation(
            message,
            policy,
            validation));
    }
}

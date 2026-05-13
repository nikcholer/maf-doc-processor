using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public static class HumanReviewEvaluator
{
    private const decimal RequiredConfidenceThreshold = 0.50m;
    private const decimal RecommendedConfidenceThreshold = 0.80m;

    public static HumanReviewResult Evaluate(
        DocumentClassification classification,
        ReceiptPolicyResult? policyResult,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        bool requiresUserAttestation = false,
        string? attestationPrompt = null)
    {
        var requiredReasons = new List<string>();
        var recommendedReasons = new List<string>();

        AddConfidenceReasons(classification, requiredReasons, recommendedReasons);
        requiredReasons.AddRange(errors.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        recommendedReasons.AddRange(warnings.Where(reason => !string.IsNullOrWhiteSpace(reason)));

        if (policyResult is { Decision: PolicyDecision.NeedsReview })
        {
            requiredReasons.AddRange(policyResult.Reasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        }

        if (requiresUserAttestation)
        {
            requiredReasons.Add(attestationPrompt ?? "User attestation is required before submission.");
        }

        var status = requiredReasons.Count > 0
            ? HumanReviewStatus.Required
            : recommendedReasons.Count > 0
                ? HumanReviewStatus.Recommended
                : HumanReviewStatus.NotRequired;
        var reasons = requiredReasons
            .Concat(recommendedReasons)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return status == HumanReviewStatus.NotRequired
            ? HumanReviewResult.NotRequired
            : new HumanReviewResult(
                status,
                reasons,
                requiresUserAttestation,
                requiresUserAttestation
                    ? attestationPrompt ?? "User attestation is required before submission."
                    : null);
    }

    private static void AddConfidenceReasons(
        DocumentClassification classification,
        ICollection<string> requiredReasons,
        ICollection<string> recommendedReasons)
    {
        if (classification.Confidence is null)
        {
            requiredReasons.Add("Classification confidence was not returned by the model.");
            return;
        }

        if (classification.Confidence < RequiredConfidenceThreshold)
        {
            requiredReasons.Add(
                $"Classification confidence {classification.Confidence:0.00} is below required threshold {RequiredConfidenceThreshold:0.00}.");
            return;
        }

        if (classification.Confidence < RecommendedConfidenceThreshold)
        {
            recommendedReasons.Add(
                $"Classification confidence {classification.Confidence:0.00} is below normal processing threshold {RecommendedConfidenceThreshold:0.00}.");
        }
    }
}

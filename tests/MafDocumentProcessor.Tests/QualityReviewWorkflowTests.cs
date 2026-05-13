using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Tests;

public sealed class QualityReviewWorkflowTests
{
    [Fact]
    public async Task RunAsync_RunsAnalystThenCriticAndCombinesUsage()
    {
        var chatClient = new SequenceModelChatClient(
            new ModelChatResponse(
                "Receipt fields look internally consistent.",
                new ModelTokenUsage("quality_analyst", "review-model", 10, 20, 30, EstimatedTotalCostUsd: 0.000003m, DurationMilliseconds: 100)),
            new ModelChatResponse(
                """{"decision":"Accept","findings":[{"severity":"Info","message":"No quality issues found."}]}""",
                new ModelTokenUsage("quality_critic", "review-model", 5, 10, 15, EstimatedTotalCostUsd: 0.0000015m, DurationMilliseconds: 80)));
        var workflow = new DocumentQualityReviewWorkflow(chatClient, CreateSettings());

        var result = await workflow.RunAsync(CreateDocumentResult());

        Assert.Equal(QualityReviewDecision.Accept, result.Decision);
        Assert.Equal(QualityReviewFindingSeverity.Info, result.Findings[0].Severity);
        Assert.Equal("No quality issues found.", result.Findings[0].Message);
        Assert.Equal(["quality_analyst", "quality_critic"], result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
        Assert.Equal(45, result.ModelUsage.TotalTokens);
        Assert.Equal(0.0000045m, result.ModelUsage.EstimatedTotalCostUsd);
        Assert.Equal(180, result.ModelUsage.TotalDurationMilliseconds);
    }

    [Fact]
    public async Task RunAsync_CanFlagDisagreementForHumanReview()
    {
        var chatClient = new SequenceModelChatClient(
            new ModelChatResponse(
                "The classification says receipt, but the parsed data has no store name.",
                new ModelTokenUsage("quality_analyst", "review-model", 10, 20, 30)),
            new ModelChatResponse(
                """{"decision":"NeedsHumanReview","findings":[{"severity":"Warning","message":"Classification and extracted fields may not agree."}]}""",
                new ModelTokenUsage("quality_critic", "review-model", 5, 10, 15)));
        var workflow = new DocumentQualityReviewWorkflow(chatClient, CreateSettings());

        var result = await workflow.RunAsync(CreateDocumentResult());

        Assert.Equal(QualityReviewDecision.NeedsHumanReview, result.Decision);
        Assert.Contains(result.Findings, finding => finding.Message.Contains("may not agree"));
    }

    [Fact]
    public async Task RunAsync_RejectsInvalidCriticJson()
    {
        var chatClient = new SequenceModelChatClient(
            new ModelChatResponse(
                "Analysis complete.",
                new ModelTokenUsage("quality_analyst", "review-model", 10, 20, 30)),
            new ModelChatResponse(
                "not json",
                new ModelTokenUsage("quality_critic", "review-model", 5, 10, 15)));
        var workflow = new DocumentQualityReviewWorkflow(chatClient, CreateSettings());

        await Assert.ThrowsAsync<DocumentModelResponseException>(
            () => workflow.RunAsync(CreateDocumentResult()));
    }

    private static DocumentProcessingResult CreateDocumentResult()
    {
        return new DocumentProcessingResult(
            DocumentCategory.Receipt,
            new DocumentMetadata(
                "receipt.png",
                "image/png",
                FileSizeBytes: 128,
                DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
                SourceId: "quality-test",
                ModelId: "review-model",
                ClassificationConfidence: 0.91m),
            new DocumentClassification(
                DocumentCategory.Receipt,
                0.91m,
                "receipt layout",
                "receipt"),
            DocumentModelUsage.FromCalls([
                new ModelTokenUsage("classification", "model", 1, 2, 3),
                new ModelTokenUsage("receipt_extraction", "model", 4, 5, 9)
            ]),
            new ReceiptData(
                "Corner Shop",
                10.5m,
                new DateOnly(2026, 5, 11),
                "Visa",
                "GBP"),
            ShoppingList: null,
            new ReceiptPolicyResult(
                IsWithinReviewThreshold: true,
                HasPaymentMethod: true,
                PolicyDecision.Approved,
                ["Receipt is within the review threshold and includes a payment method."]),
            ValidationResult.Valid,
            HumanReviewResult.NotRequired,
            IsSuccess: true,
            Errors: [],
            Warnings: []);
    }

    private static ModelRoleSettings CreateSettings()
    {
        return new ModelRoleSettings(
            "TestProvider",
            "https://example.invalid",
            "review-model",
            "TEST_API_KEY",
            "quality-review");
    }

    private sealed class SequenceModelChatClient(params ModelChatResponse[] responses) : IModelChatClient
    {
        private int _index;

        public ValueTask<ModelChatResponse> CompleteAsync(
            ModelChatRequest request,
            CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(_index, responses.Length - 1)];
            _index++;
            return ValueTask.FromResult(response);
        }
    }
}

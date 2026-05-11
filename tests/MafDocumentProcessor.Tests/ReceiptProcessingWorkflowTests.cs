using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Tests;

public sealed class ReceiptProcessingWorkflowTests
{
    [Fact]
    public async Task RunAsync_ProcessesReceiptEndToEnd()
    {
        var workflow = new ReceiptProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.91m),
            new FakeReceiptExtractor(new ReceiptData(
                "Meadow Vale Supermarket",
                21.02m,
                new DateOnly(2024, 5, 28),
                "Visa",
                "GBP")),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.Receipt, result.Category);
        Assert.Equal("Meadow Vale Supermarket", result.Receipt?.StoreName);
        Assert.Equal(21.02m, result.Receipt?.TotalAmount);
        Assert.Equal(PolicyDecision.Approved, result.PolicyResult?.Decision);
        Assert.True(result.PolicyResult?.IsWithinReviewThreshold);
        Assert.True(result.PolicyResult?.HasPaymentMethod);
        Assert.True(result.Validation.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.ModelUsage.Calls.Count);
        Assert.Equal(15, result.ModelUsage.TotalTokens);
        Assert.Equal(0.91m, result.Metadata.ClassificationConfidence);
    }

    [Fact]
    public async Task RunAsync_FlagsReceiptForReviewWhenPaymentMethodIsMissing()
    {
        var workflow = new ReceiptProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.82m),
            new FakeReceiptExtractor(new ReceiptData(
                "Corner Shop",
                10.50m,
                new DateOnly(2024, 6, 1),
                PaymentMethod: null,
                "GBP")),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.False(result.Validation.IsValid);
        Assert.Equal(PolicyDecision.NeedsReview, result.PolicyResult?.Decision);
        Assert.Contains(result.Warnings, warning => warning.Contains("payment method is missing"));
    }

    [Fact]
    public async Task RunAsync_FlagsReceiptForReviewWhenExtractedFieldsFailValidation()
    {
        var workflow = new ReceiptProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.82m),
            new FakeReceiptExtractor(new ReceiptData(
                StoreName: "",
                TotalAmount: 10.50m,
                new DateOnly(2024, 6, 1),
                "Visa",
                CurrencyCode: "GB")),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.False(result.Validation.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("store name is missing"));
        Assert.Contains(result.Warnings, warning => warning.Contains("three-letter ISO-4217"));
    }

    [Fact]
    public async Task RunAsync_ReturnsHumanUnsupportedMessageForNonReceipt()
    {
        var workflow = new ReceiptProcessingWorkflow(
            new FakeDocumentClassifier(
                DocumentCategory.Unknown,
                0.79m,
                "car registration document"),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(DocumentCategory.Unknown, result.Category);
        Assert.Null(result.Receipt);
        Assert.Contains(
            result.Errors,
            error => error == "This appears to be a car registration document. This demo can only process receipts right now.");
        Assert.Single(result.ModelUsage.Calls);
    }

    [Fact]
    public async Task RunAsync_ReturnsHumanUnsupportedMessageForInvoice()
    {
        var workflow = new ReceiptProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Invoice, 0.95m),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error == "This appears to be an invoice. This demo can only process receipts right now.");
    }

    private static FileRequest CreateReceiptRequest()
    {
        return new FileRequest(
            [1, 2, 3],
            "receipt.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2024-05-28T12:00:00Z"),
            SourceId: "unit-test");
    }

    private sealed class FakeDocumentClassifier(
        DocumentCategory category,
        decimal? confidence,
        string? documentTypeDescription = null) : IDocumentClassifier
    {
        public ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<DocumentClassification>(
                new DocumentClassification(
                    category,
                    confidence,
                    "test classification",
                    documentTypeDescription),
                new ModelTokenUsage("classification", "test-classifier", 1, 2, 3)));
        }
    }

    private sealed class FakeReceiptExtractor(ReceiptData receipt) : IReceiptExtractor
    {
        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt,
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12)));
        }
    }
}

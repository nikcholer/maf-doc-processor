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
        var shoppingListExtractor = new FakeShoppingListExtractor(CreateShoppingList());
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.91m),
            new FakeReceiptExtractor(new ReceiptData(
                "Meadow Vale Supermarket",
                21.02m,
                new DateOnly(2024, 5, 28),
                "Visa",
                "GBP")),
            shoppingListExtractor,
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor());

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.Receipt, result.Category);
        Assert.Equal("Meadow Vale Supermarket", result.Receipt?.StoreName);
        Assert.Equal(21.02m, result.Receipt?.TotalAmount);
        Assert.Equal(PolicyDecision.Approved, result.PolicyResult?.Decision);
        Assert.True(result.PolicyResult?.IsWithinReviewThreshold);
        Assert.True(result.PolicyResult?.HasPaymentMethod);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.NotRequired, result.HumanReview.Status);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.ModelUsage.Calls.Count);
        Assert.Equal(15, result.ModelUsage.TotalTokens);
        Assert.Equal(0.000006m, result.ModelUsage.EstimatedTotalCostUsd);
        Assert.Equal(0.91m, result.Metadata.ClassificationConfidence);
        Assert.Equal(0, shoppingListExtractor.CallCount);
    }

    [Fact]
    public async Task RunAsync_FlagsReceiptForReviewWhenPaymentMethodIsMissing()
    {
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.82m),
            new FakeReceiptExtractor(new ReceiptData(
                "Corner Shop",
                10.50m,
                new DateOnly(2024, 6, 1),
                PaymentMethod: null,
                "GBP")));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.False(result.Validation.IsValid);
        Assert.Equal(PolicyDecision.NeedsReview, result.PolicyResult?.Decision);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Contains(result.HumanReview.Reasons, reason => reason.Contains("payment method is missing"));
        Assert.Contains(result.Warnings, warning => warning.Contains("payment method is missing"));
    }

    [Fact]
    public async Task RunAsync_FlagsReceiptForReviewWhenExtractedFieldsFailValidation()
    {
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.82m),
            new FakeReceiptExtractor(new ReceiptData(
                StoreName: "",
                TotalAmount: 10.50m,
                new DateOnly(2024, 6, 1),
                "Visa",
                CurrencyCode: "GB")));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.False(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.Recommended, result.HumanReview.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("store name is missing"));
        Assert.Contains(result.Warnings, warning => warning.Contains("three-letter ISO-4217"));
    }

    [Fact]
    public async Task RunAsync_ReExtractsReceiptOnceWhenValidationFails()
    {
        var receiptExtractor = new SequenceReceiptExtractor(
            new ReceiptData(
                StoreName: "",
                TotalAmount: 10.50m,
                new DateOnly(2024, 6, 1),
                "Visa",
                CurrencyCode: "GB"),
            new ReceiptData(
                "Corner Shop",
                10.50m,
                new DateOnly(2024, 6, 1),
                "Visa",
                "GBP"));
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.82m),
            receiptExtractor);

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.NotRequired, result.HumanReview.Status);
        Assert.Equal("Corner Shop", result.Receipt?.StoreName);
        Assert.Equal(2, receiptExtractor.CallCount);
        Assert.Contains(receiptExtractor.LastRepairInstructions, reason => reason.Contains("store name is missing"));
        Assert.Equal(3, result.ModelUsage.Calls.Count);
        Assert.Equal(27, result.ModelUsage.TotalTokens);
    }

    [Fact]
    public async Task RunAsync_RoutesShoppingListToShoppingListExtractor()
    {
        var receiptExtractor = new FakeReceiptExtractor(new ReceiptData(
            "Not used",
            1m,
            null,
            null,
            "GBP"));
        var shoppingListExtractor = new FakeShoppingListExtractor(CreateShoppingList());
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ShoppingList, 0.88m),
            receiptExtractor,
            shoppingListExtractor,
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor());

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.ShoppingList, result.Category);
        Assert.Null(result.Receipt);
        Assert.Null(result.PolicyResult);
        Assert.NotNull(result.ShoppingList);
        Assert.Equal("Weekly groceries", result.ShoppingList.Title);
        Assert.Equal(["milk", "bread"], result.ShoppingList.Items.Select(item => item.Name).ToArray());
        Assert.True(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.NotRequired, result.HumanReview.Status);
        Assert.Equal(1, shoppingListExtractor.CallCount);
        Assert.Equal(0, receiptExtractor.CallCount);
    }

    [Fact]
    public async Task RunAsync_ReExtractsShoppingListOnceWhenValidationFails()
    {
        var receiptExtractor = new FakeReceiptExtractor(new ReceiptData(
            "Not used",
            1m,
            null,
            null,
            "GBP"));
        var shoppingListExtractor = new SequenceShoppingListExtractor(
            new ShoppingListData(Title: null, Items: [], Notes: null),
            CreateShoppingList());
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ShoppingList, 0.88m),
            receiptExtractor,
            shoppingListExtractor,
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor());

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(HumanReviewStatus.NotRequired, result.HumanReview.Status);
        Assert.NotNull(result.ShoppingList);
        Assert.Equal(["milk", "bread"], result.ShoppingList.Items.Select(item => item.Name).ToArray());
        Assert.Equal(2, shoppingListExtractor.CallCount);
        Assert.Contains(shoppingListExtractor.LastRepairInstructions, reason => reason.Contains("no readable items"));
        Assert.Equal(3, result.ModelUsage.Calls.Count);
        Assert.Equal(27, result.ModelUsage.TotalTokens);
    }

    [Fact]
    public async Task RunAsync_UnwrapsExecutorExceptions()
    {
        var expected = new DocumentModelResponseException("The shopping list extraction model returned invalid JSON.");
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ShoppingList, 0.88m),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")),
            new ThrowingShoppingListExtractor(expected),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor());

        var exception = await Assert.ThrowsAsync<DocumentModelResponseException>(
            () => workflow.RunAsync(CreateReceiptRequest()));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task RunAsync_ReturnsHumanUnsupportedMessageForNonReceipt()
    {
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(
                DocumentCategory.Unknown,
                0.79m,
                "car registration document"),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(DocumentCategory.Unknown, result.Category);
        Assert.Null(result.Receipt);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Contains(
            result.Errors,
            error => error == "This appears to be a car registration document. This demo can process receipts, shopping lists, and Sujiko puzzles right now.");
        Assert.Single(result.ModelUsage.Calls);
    }

    [Fact]
    public async Task RunAsync_ReturnsHumanUnsupportedMessageForInvoice()
    {
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Invoice, 0.95m),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Contains(
            result.Errors,
            error => error == "This appears to be an invoice. This demo can process receipts, shopping lists, and Sujiko puzzles right now.");
    }

    [Fact]
    public async Task RunAsync_RecommendsReviewForLowConfidenceSupportedClassification()
    {
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.Receipt, 0.72m),
            new FakeReceiptExtractor(new ReceiptData(
                "Corner Shop",
                10.50m,
                new DateOnly(2024, 6, 1),
                "Visa",
                "GBP")));

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(HumanReviewStatus.Recommended, result.HumanReview.Status);
        Assert.Contains(result.HumanReview.Reasons, reason => reason.Contains("below normal processing threshold"));
    }

    [Fact]
    public async Task RunAsync_RoutesSujikoPuzzleToSujikoExtractor()
    {
        var receiptExtractor = new FakeReceiptExtractor(new ReceiptData(
            "Not used",
            1m,
            null,
            null,
            "GBP"));
        var shoppingListExtractor = new FakeShoppingListExtractor(CreateShoppingList());
        var sujikoExtractor = new FakeSujikoPuzzleExtractor(CreateSujikoPuzzle());
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.SujikoPuzzle, 0.93m),
            receiptExtractor,
            shoppingListExtractor,
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor(),
            sujikoExtractor);

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.SujikoPuzzle, result.Category);
        Assert.Null(result.Receipt);
        Assert.Null(result.ShoppingList);
        Assert.Null(result.PolicyResult);
        Assert.Equal(21, result.SujikoPuzzle?.QuadrantTotals.TopLeft);
        Assert.Equal(12, result.SujikoPuzzle?.QuadrantTotals.TopRight);
        Assert.Equal(new SujikoCellValue(2, 2, 1), result.SujikoPuzzle?.GivenCells[0]);
        Assert.Equal(1, sujikoExtractor.CallCount);
        Assert.Equal(0, receiptExtractor.CallCount);
        Assert.Equal(0, shoppingListExtractor.CallCount);
        Assert.Equal(HumanReviewStatus.NotRequired, result.HumanReview.Status);
    }

    [Fact]
    public async Task RunAsync_ReExtractsSujikoPuzzleOnceWhenValidationFails()
    {
        var sujikoExtractor = new SequenceSujikoPuzzleExtractor(
            new SujikoPuzzleData(
                new SujikoQuadrantTotals(21, 12, 21, 17),
                [
                    new SujikoCellValue(Row: 4, Column: 2, Value: 8),
                    new SujikoCellValue(Row: 3, Column: 2, Value: 8)
                ]),
            CreateSujikoPuzzle());
        var workflow = new DocumentProcessingWorkflow(
            new FakeDocumentClassifier(DocumentCategory.SujikoPuzzle, 0.93m),
            new FakeReceiptExtractor(new ReceiptData(
                "Not used",
                1m,
                null,
                null,
                "GBP")),
            new FakeShoppingListExtractor(CreateShoppingList()),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor(),
            sujikoExtractor);

        var result = await workflow.RunAsync(CreateReceiptRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(2, sujikoExtractor.CallCount);
        Assert.Contains(sujikoExtractor.LastRepairInstructions, reason => reason.Contains("outside the 1-3 grid"));
        Assert.Contains(sujikoExtractor.LastRepairInstructions, reason => reason.Contains("given value 8"));
        Assert.Equal(new SujikoCellValue(3, 2, 8), result.SujikoPuzzle?.GivenCells[1]);
        Assert.Equal(3, result.ModelUsage.Calls.Count);
    }

    private static DocumentProcessingWorkflow CreateWorkflow(
        IDocumentClassifier classifier,
        IReceiptExtractor receiptExtractor)
    {
        return new DocumentProcessingWorkflow(
            classifier,
            receiptExtractor,
            new FakeShoppingListExtractor(CreateShoppingList()),
            new ReceiptPolicyOptions(ReviewThreshold: 50m, DefaultCurrencyCode: "GBP"),
            new PassThroughImagePreprocessor());
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

    private static ShoppingListData CreateShoppingList()
    {
        return new ShoppingListData(
            "Weekly groceries",
            [
                new ShoppingListItem("milk", 2, "pints", false),
                new ShoppingListItem("bread", null, null, null)
            ],
            Notes: null);
    }

    private static SujikoPuzzleData CreateSujikoPuzzle()
    {
        return new SujikoPuzzleData(
            new SujikoQuadrantTotals(21, 12, 21, 17),
            [
                new SujikoCellValue(2, 2, 1),
                new SujikoCellValue(3, 2, 8)
            ]);
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
                new ModelTokenUsage("classification", "test-classifier", 1, 2, 3, 0.20m, 0.50m, 0.0000002m, 0.000001m, 0.0000012m)));
        }
    }

    private sealed class FakeReceiptExtractor(ReceiptData receipt) : IReceiptExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt,
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class FakeShoppingListExtractor(ShoppingListData shoppingList) : IShoppingListExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                shoppingList,
                new ModelTokenUsage("shopping_list_extraction", "test-shopping-list-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class FakeSujikoPuzzleExtractor(SujikoPuzzleData puzzle) : ISujikoPuzzleExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                puzzle,
                new ModelTokenUsage("sujiko_puzzle_extraction", "test-sujiko-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class SequenceReceiptExtractor(params ReceiptData[] receipts) : IReceiptExtractor
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string> LastRepairInstructions { get; private set; } = [];

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (repairInstructions is not null)
            {
                LastRepairInstructions = repairInstructions;
            }

            var receipt = receipts[Math.Min(CallCount, receipts.Length - 1)];
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                receipt,
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class SequenceShoppingListExtractor(params ShoppingListData[] shoppingLists) : IShoppingListExtractor
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string> LastRepairInstructions { get; private set; } = [];

        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (repairInstructions is not null)
            {
                LastRepairInstructions = repairInstructions;
            }

            var shoppingList = shoppingLists[Math.Min(CallCount, shoppingLists.Length - 1)];
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ShoppingListData>(
                shoppingList,
                new ModelTokenUsage("shopping_list_extraction", "test-shopping-list-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class SequenceSujikoPuzzleExtractor(params SujikoPuzzleData[] puzzles) : ISujikoPuzzleExtractor
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string> LastRepairInstructions { get; private set; } = [];

        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (repairInstructions is not null)
            {
                LastRepairInstructions = repairInstructions;
            }

            var puzzle = puzzles[Math.Min(CallCount, puzzles.Length - 1)];
            CallCount++;
            return ValueTask.FromResult(new ModelResult<SujikoPuzzleData>(
                puzzle,
                new ModelTokenUsage("sujiko_puzzle_extraction", "test-sujiko-extractor", 4, 8, 12, 0.20m, 0.50m, 0.0000008m, 0.000004m, 0.0000048m)));
        }
    }

    private sealed class ThrowingShoppingListExtractor(Exception exception) : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw exception;
        }
    }

    private sealed class PassThroughImagePreprocessor : IModelImagePreprocessor
    {
        public ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
            FileRequest request,
            ModelImagePreprocessingPurpose purpose,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ModelImagePreprocessingResult(
                request,
                purpose,
                WasResized: false,
                OriginalWidth: 1,
                OriginalHeight: 1,
                Width: 1,
                Height: 1,
                request.FileSizeBytes,
                request.FileSizeBytes));
        }
    }
}

using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Tests;

public sealed class ExpenseReportProcessingWorkflowTests
{
    [Fact]
    public async Task RunAsync_ProcessesValidExpenseReportWithAttestationReview()
    {
        var receiptExtractor = new FakeReceiptExtractor();
        var expenseExtractor = new FakeExpenseReportExtractor(CreateValidExpenseReport());
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ExpenseReport, 0.97m),
            receiptExtractor,
            expenseExtractor);

        var result = await workflow.RunAsync(CreateRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentCategory.ExpenseReport, result.Category);
        Assert.Null(result.Receipt);
        Assert.Null(result.PolicyResult);
        Assert.NotNull(result.ExpenseReport);
        Assert.Equal("ER-2026-014", result.ExpenseReport.ReportNumber);
        Assert.Equal(48.50m, result.ExpenseReport.ClaimedTotal);
        Assert.Equal(["R-001", "R-002"], result.ExpenseReport.Lines.Select(line => line.ReceiptReference));
        Assert.True(result.Validation.IsValid);
        Assert.Equal(PolicyDecision.Approved, result.ExpensePolicy?.Decision);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.True(result.HumanReview.RequiresUserAttestation);
        Assert.Equal(ExpenseReportResultExecutor.AttestationPrompt, result.HumanReview.AttestationPrompt);
        Assert.Equal(
            [ExpenseReportResultExecutor.AttestationPrompt],
            result.HumanReview.Reasons);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(1, expenseExtractor.CallCount);
        Assert.Equal(0, receiptExtractor.CallCount);
        Assert.Equal(
            ["classification", "expense_report_extraction"],
            result.ModelUsage.Calls.Select(call => call.Operation).ToArray());
    }

    [Fact]
    public async Task RunAsync_RejectsArithmeticMismatchWithoutAcceptingTheModelSum()
    {
        var expenseExtractor = new FakeExpenseReportExtractor(CreateValidExpenseReport() with
        {
            ClaimedTotal = 60.00m
        });
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ExpenseReport, 0.97m),
            new FakeReceiptExtractor(),
            expenseExtractor);

        var result = await workflow.RunAsync(CreateRequest());

        Assert.False(result.IsSuccess);
        Assert.False(result.Validation.IsValid);
        Assert.Contains(
            ExpenseReportValidationExecutor.ArithmeticMismatchReason,
            result.Errors);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Equal(2, expenseExtractor.CallCount);
        Assert.Equal(3, result.ModelUsage.Calls.Count);
        Assert.Equal(48.50m, result.ExpenseReport?.Lines.Sum(line => line.Amount));
        Assert.Equal(60.00m, result.ExpenseReport?.ClaimedTotal);
    }

    [Fact]
    public async Task RunAsync_ReExtractsExpenseReportOnceWhenValidationFails()
    {
        var expenseExtractor = new SequenceExpenseReportExtractor(
            CreateValidExpenseReport() with
            {
                Lines =
                [
                    new ExpenseReportLine(new DateOnly(2026, 8, 4), "Train fare", null, 18.50m, "R-101")
                ]
            },
            CreateValidExpenseReport() with
            {
                ReportNumber = "ER-2026-016",
                Lines =
                [
                    new ExpenseReportLine(new DateOnly(2026, 8, 4), "Train fare", null, 18.50m, "R-101"),
                    new ExpenseReportLine(new DateOnly(2026, 8, 12), "Client lunch", null, 30.00m, "R-102")
                ]
            });
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ExpenseReport, 0.96m),
            new FakeReceiptExtractor(),
            expenseExtractor);

        var result = await workflow.RunAsync(CreateRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(2, result.ExpenseReport?.Lines.Count);
        Assert.Equal(2, expenseExtractor.CallCount);
        Assert.Contains(
            expenseExtractor.LastRepairInstructions,
            reason => reason == ExpenseReportValidationExecutor.ArithmeticMismatchReason);
        Assert.Equal(3, result.ModelUsage.Calls.Count);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
    }

    [Fact]
    public async Task RunAsync_FlagsHighValueExpenseWithoutReceiptReferenceForReview()
    {
        var report = new ExpenseReportData(
            "ER-2026-017",
            "EXPENSE REPORT",
            "Alex Example",
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 20),
            "GBP",
            480.00m,
            [
                new ExpenseReportLine(
                    new DateOnly(2026, 8, 19),
                    "Conference hotel",
                    null,
                    480.00m,
                    ReceiptReference: null)
            ],
            Notes: "Receipt reference: not shown",
            VisibleApprovalStatus: "awaiting manager review");
        var workflow = CreateWorkflow(
            new FakeDocumentClassifier(DocumentCategory.ExpenseReport, 0.96m),
            new FakeReceiptExtractor(),
            new FakeExpenseReportExtractor(report));

        var result = await workflow.RunAsync(CreateRequest());

        Assert.True(result.IsSuccess);
        Assert.True(result.Validation.IsValid);
        Assert.Equal(PolicyDecision.NeedsReview, result.ExpensePolicy?.Decision);
        Assert.Equal(HumanReviewStatus.Required, result.HumanReview.Status);
        Assert.Contains(ExpenseReportResultExecutor.AttestationPrompt, result.HumanReview.Reasons);
        Assert.Contains(ExpenseReportPolicyExecutor.HighValueReviewReason, result.HumanReview.Reasons);
        Assert.Contains(
            ExpenseReportPolicyExecutor.MissingReceiptReferenceReason,
            result.HumanReview.Reasons);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    private static DocumentProcessingWorkflow CreateWorkflow(
        IDocumentClassifier classifier,
        IReceiptExtractor receiptExtractor,
        IExpenseReportExtractor expenseReportExtractor)
    {
        return new DocumentProcessingWorkflow(
            classifier,
            receiptExtractor,
            new FakeShoppingListExtractor(),
            new ReceiptPolicyOptions(),
            new PassThroughImagePreprocessor(),
            new FakeSujikoPuzzleExtractor(),
            expenseReportExtractor,
            new ExpensePolicyOptions(HighValueReviewThreshold: 250m));
    }

    private static FileRequest CreateRequest()
    {
        return new FileRequest(
            [1, 2, 3],
            "expense-report.png",
            "image/png",
            FileSizeBytes: 3,
            DateTimeOffset.Parse("2026-08-26T12:00:00Z"),
            SourceId: "expense-unit-test");
    }

    private static ExpenseReportData CreateValidExpenseReport()
    {
        return new ExpenseReportData(
            "ER-2026-014",
            "EXPENSE REPORT",
            "Alex Example",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 20),
            "GBP",
            48.50m,
            [
                new ExpenseReportLine(new DateOnly(2026, 8, 4), "Train fare", null, 18.50m, "R-001"),
                new ExpenseReportLine(new DateOnly(2026, 8, 12), "Client lunch", null, 30.00m, "R-002")
            ],
            "Synthetic valid example.",
            "Not yet submitted");
    }

    private sealed class FakeDocumentClassifier(
        DocumentCategory category,
        decimal? confidence) : IDocumentClassifier
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
                    "expense report"),
                new ModelTokenUsage("classification", "test-classifier", 1, 2, 3)));
        }
    }

    private sealed class FakeReceiptExtractor : IReceiptExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ReceiptData>(
                new ReceiptData("Not used", 1m, null, null, "GBP"),
                new ModelTokenUsage("receipt_extraction", "test-extractor", 4, 8, 12)));
        }
    }

    private sealed class FakeShoppingListExtractor : IShoppingListExtractor
    {
        public ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Shopping-list extraction should not run.");
        }
    }

    private sealed class FakeSujikoPuzzleExtractor : ISujikoPuzzleExtractor
    {
        public ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            throw new InvalidOperationException("Sujiko extraction should not run.");
        }
    }

    private sealed class FakeExpenseReportExtractor(ExpenseReportData expenseReport)
        : IExpenseReportExtractor
    {
        public int CallCount { get; private set; }

        public ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ExpenseReportData>(
                expenseReport,
                new ModelTokenUsage("expense_report_extraction", "test-expense-extractor", 4, 8, 12)));
        }
    }

    private sealed class SequenceExpenseReportExtractor(params ExpenseReportData[] reports)
        : IExpenseReportExtractor
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string> LastRepairInstructions { get; private set; } = [];

        public ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
            FileRequest request,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? repairInstructions = null)
        {
            if (repairInstructions is not null)
            {
                LastRepairInstructions = repairInstructions;
            }

            var report = reports[Math.Min(CallCount, reports.Length - 1)];
            CallCount++;
            return ValueTask.FromResult(new ModelResult<ExpenseReportData>(
                report,
                new ModelTokenUsage("expense_report_extraction", "test-expense-extractor", 4, 8, 12)));
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

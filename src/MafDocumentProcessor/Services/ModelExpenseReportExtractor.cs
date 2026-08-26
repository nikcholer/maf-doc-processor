using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelExpenseReportExtractor(
    IModelChatClient chatClient,
    ModelRoleSettings settings) : IExpenseReportExtractor
{
    private const string Operation = "expense_report_extraction";

    public async ValueTask<ModelResult<ExpenseReportData>> ExtractExpenseReportAsync(
        FileRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You extract expense-report fields from document images.
                    Identify the main document occupying most of the image, including its centre. Ignore fragments of neighbouring documents at the edges.
                    Do not explain, reason aloud, use markdown, or include any text outside the JSON object.
                    Return only compact JSON with this exact shape:
                    {"reportNumber":"string|null","title":"string|null","claimantName":"string|null","periodStart":"yyyy-MM-dd|null","periodEnd":"yyyy-MM-dd|null","currencyCode":"GBP","claimedTotal":0.0,"notes":"string|null","visibleApprovalStatus":"string|null","lines":[{"date":"yyyy-MM-dd|null","description":"string","category":"string|null","amount":0.0,"receiptReference":"string|null"}]}
                    Extract only values visible on the expense report. Use null for optional fields that are not visible. Do not invent lines, receipt references, currencies, or approval. Do not calculate whether the claim should be accepted, matched, or submitted. Copy receipt references only when they are printed on the report.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            BuildUserInstruction(request, repairInstructions)),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 1200),
            cancellationToken);

        return new ModelResult<ExpenseReportData>(
            ModelResponseParsers.ParseExpenseReport(response.Content),
            response.Usage);
    }

    private static string BuildUserInstruction(
        FileRequest request,
        IReadOnlyList<string>? repairInstructions)
    {
        var instruction =
            $"{DocumentImagePrompts.MainDocumentFocus} Extract expense-report fields from this uploaded image. File: {request.FileName}; content type: {request.ContentType}.";
        if (repairInstructions is not { Count: > 0 })
        {
            return instruction;
        }

        return string.Join(
            Environment.NewLine,
            instruction,
            "A previous extraction failed validation for:",
            string.Join(Environment.NewLine, repairInstructions.Select(reason => $"- {reason}")),
            "Re-extract from the image, correcting those validation failures only when the document visibly supports the correction. Return JSON only. Do not judge arithmetic, ownership, or whether the claim should be submitted.");
    }
}
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelSujikoPuzzleExtractor(
    IModelChatClient chatClient,
    ModelRoleSettings settings) : ISujikoPuzzleExtractor
{
    private const string Operation = "sujiko_puzzle_extraction";

    public async ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
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
                    You extract Sujiko puzzle starting states from newspaper-style puzzle images.
                    A Sujiko puzzle is a 3x3 grid. Four circled quadrant totals appear at the internal grid intersections.
                    The quadrant totals are named topLeft, topRight, bottomLeft, and bottomRight by their position in the 2x2 set of circled totals.
                    Zero or more given cell values may appear in the 3x3 cells. Cell row and column numbers are 1-based from top-left to bottom-right.
                    Do not solve the puzzle or infer missing cell values.
                    Return only compact JSON with this exact shape:
                    {"quadrantTotals":{"topLeft":0,"topRight":0,"bottomLeft":0,"bottomRight":0},"givenCells":[{"row":1,"column":1,"value":0}]}
                    Use an empty givenCells array when no cell values are printed.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(BuildUserInstruction(request, repairInstructions)),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 500),
            cancellationToken);

        return new ModelResult<SujikoPuzzleData>(
            ModelResponseParsers.ParseSujikoPuzzle(response.Content),
            response.Usage);
    }

    private static string BuildUserInstruction(
        FileRequest request,
        IReadOnlyList<string>? repairInstructions)
    {
        var instruction =
            $"Extract the Sujiko puzzle starting state from this uploaded image. File: {request.FileName}; content type: {request.ContentType}.";
        if (repairInstructions is not { Count: > 0 })
        {
            return instruction;
        }

        return string.Join(
            Environment.NewLine,
            instruction,
            "A previous extraction failed validation for:",
            string.Join(Environment.NewLine, repairInstructions.Select(reason => $"- {reason}")),
            "Re-extract from the image, correcting those validation failures only when the puzzle visibly supports the correction. Return JSON only.");
    }
}

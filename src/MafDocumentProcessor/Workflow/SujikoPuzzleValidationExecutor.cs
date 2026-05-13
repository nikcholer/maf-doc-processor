using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class SujikoPuzzleValidationExecutor()
    : Executor<SujikoPuzzleExtraction, ValidatedSujikoPuzzleExtraction>("SujikoPuzzleValidation")
{
    public override ValueTask<ValidatedSujikoPuzzleExtraction> HandleAsync(
        SujikoPuzzleExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(message.SujikoPuzzle);

        return ValueTask.FromResult(new ValidatedSujikoPuzzleExtraction(message, validation));
    }

    public static ValidationResult Validate(SujikoPuzzleData puzzle)
    {
        var reasons = new List<string>();
        ValidateTotal(puzzle.QuadrantTotals.TopLeft, "top-left", reasons);
        ValidateTotal(puzzle.QuadrantTotals.TopRight, "top-right", reasons);
        ValidateTotal(puzzle.QuadrantTotals.BottomLeft, "bottom-left", reasons);
        ValidateTotal(puzzle.QuadrantTotals.BottomRight, "bottom-right", reasons);

        foreach (var cell in puzzle.GivenCells)
        {
            if (cell.Row is < 1 or > 3)
            {
                reasons.Add($"Sujiko given cell row {cell.Row} is outside the 1-3 grid.");
            }

            if (cell.Column is < 1 or > 3)
            {
                reasons.Add($"Sujiko given cell column {cell.Column} is outside the 1-3 grid.");
            }

            if (cell.Value is < 1 or > 9)
            {
                reasons.Add($"Sujiko given cell value {cell.Value} is outside the 1-9 puzzle range.");
            }
        }

        var duplicateLocations = puzzle.GivenCells
            .GroupBy(cell => (cell.Row, cell.Column))
            .Where(group => group.Count() > 1)
            .Select(group => $"r{group.Key.Row}c{group.Key.Column}");
        foreach (var location in duplicateLocations)
        {
            reasons.Add($"Sujiko given cell {location} was returned more than once.");
        }

        var duplicateValues = puzzle.GivenCells
            .GroupBy(cell => cell.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var value in duplicateValues)
        {
            reasons.Add($"Sujiko given value {value} was returned more than once.");
        }

        return reasons.Count == 0
            ? ValidationResult.Valid
            : new ValidationResult(false, reasons);
    }

    private static void ValidateTotal(int total, string name, ICollection<string> reasons)
    {
        if (total <= 0)
        {
            reasons.Add($"Sujiko {name} quadrant total must be positive.");
        }
    }
}

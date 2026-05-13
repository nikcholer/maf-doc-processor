namespace MafDocumentProcessor.Domain;

public sealed record SujikoPuzzleData(
    SujikoQuadrantTotals QuadrantTotals,
    IReadOnlyList<SujikoCellValue> GivenCells);

public sealed record SujikoQuadrantTotals(
    int TopLeft,
    int TopRight,
    int BottomLeft,
    int BottomRight);

public sealed record SujikoCellValue(
    int Row,
    int Column,
    int Value);

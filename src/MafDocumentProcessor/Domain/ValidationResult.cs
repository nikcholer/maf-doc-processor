namespace MafDocumentProcessor.Domain;

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<string> Reasons)
{
    public static ValidationResult Valid { get; } = new(true, []);

    public static ValidationResult Invalid(params string[] reasons)
    {
        return new ValidationResult(false, reasons);
    }
}

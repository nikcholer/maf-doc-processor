namespace MafDocumentProcessor.Domain;

public sealed record ShoppingListData(
    string? Title,
    IReadOnlyList<ShoppingListItem> Items,
    string? Notes);

public sealed record ShoppingListItem(
    string Name,
    decimal? Quantity,
    string? Unit,
    bool? IsChecked);

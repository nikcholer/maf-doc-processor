using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IShoppingListExtractor
{
    ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
        FileRequest request,
        CancellationToken cancellationToken);
}

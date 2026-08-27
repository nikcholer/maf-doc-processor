using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MafDocumentProcessor.Api.OpenApi;

internal sealed class ProcessedDocumentDataSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly Type[] DataTypes =
    [
        typeof(ReceiptData),
        typeof(ShoppingListData),
        typeof(SujikoPuzzleData),
        typeof(ExpenseReportData)
    ];

    public async Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(ProcessedDocumentResponse)
            || context.Document is not { } document
            || schema.Properties is null
            || !schema.Properties.ContainsKey("data"))
        {
            return;
        }

        var oneOf = new List<IOpenApiSchema>();
        var dataSchema = new OpenApiSchema
        {
            OneOf = oneOf,
            Description =
                "The parsed data shape is selected by the enclosing category: Receipt uses ReceiptData, " +
                "ShoppingList uses ShoppingListData, SujikoPuzzle uses SujikoPuzzleData, and ExpenseReport " +
                "uses ExpenseReportData."
        };
        schema.Properties["data"] = dataSchema;

        foreach (var dataType in DataTypes)
        {
            var componentName = dataType.Name;
            var componentSchema = await context.GetOrCreateSchemaAsync(
                dataType,
                parameterDescription: null,
                cancellationToken);
            document.AddComponent(componentName, componentSchema);
            oneOf.Add(new OpenApiSchemaReference(componentName, document));
        }
    }
}

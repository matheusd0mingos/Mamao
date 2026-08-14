using Mamao.SharedKernel;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mamao.Api;

/// <summary>
/// Declara todo <see cref="IStronglyTypedId"/> como string/uuid no documento OpenAPI.
///
/// Sem isto o gerador nao sabe descrever um <c>readonly record struct</c> com JsonConverter
/// proprio e emite schema vazio, que vira <c>unknown</c> no TypeScript. O contrato passa a
/// dizer "id: unknown" e o frontend perde a checagem exatamente no campo que mais circula —
/// sem nenhum erro de build para avisar.
///
/// Fica no transformer, e nao no normalize-openapi.mjs, porque isto e uma verdade sobre a
/// API e nao um conserto de gerador: quem consumir o documento direto merece a mesma
/// informacao que o nosso frontend.
/// </summary>
internal sealed class StronglyTypedIdSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        // Desembrulhar Nullable<> nao e detalhe: um id OPCIONAL (DepartmentId?) chega aqui
        // como Nullable<DepartmentId>, que nao implementa a marca. Sem isto, o id que
        // aparece primeiro num campo opcional continuava saindo como `unknown` — e so
        // esse. Erro que passa despercebido justamente por ser parcial.
        var tipo = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (!typeof(IStronglyTypedId).IsAssignableFrom(tipo))
            return Task.CompletedTask;

        schema.Type = JsonSchemaType.String;
        schema.Format = "uuid";
        schema.Properties?.Clear();

        return Task.CompletedTask;
    }
}

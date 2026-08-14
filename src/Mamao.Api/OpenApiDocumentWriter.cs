using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mamao.Api;

/// <summary>
/// Escreve o documento OpenAPI em arquivo e encerra. E o passo que alimenta o gerador do
/// cliente TypeScript no CI — DTO escrito a mao diverge do C# em silencio.
/// Ver docs/adr/0009-cliente-gerado-do-openapi.md.
///
/// Uso: dotnet run --project src/Mamao.Api -- --generate-openapi web/openapi.json
///
/// O host precisa iniciar de fato: os endpoints so sao materializados quando o pipeline de
/// roteamento e construido. Sobe numa porta efemera do loopback e para em seguida — nao
/// toca no banco, porque nenhum servico hospedado da API depende dele.
/// </summary>
public static class OpenApiDocumentWriter
{
    private const string Flag = "--generate-openapi";
    private const string DocumentName = "v1";

    public static async Task<bool> TryWriteAndExitAsync(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, Flag);
        if (index < 0)
            return false;

        var path = index + 1 < args.Length ? args[index + 1] : "openapi.json";
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");

        await app.StartAsync();

        try
        {
            var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName);
            var document = await provider.GetOpenApiDocumentAsync();

            await using var stream = File.Create(fullPath);
            await document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_0);
        }
        finally
        {
            await app.StopAsync();
        }

        app.Logger.LogInformation("OpenAPI escrito em {Path}.", fullPath);
        return true;
    }
}

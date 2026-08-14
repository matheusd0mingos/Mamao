using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Organization;

/// <summary>
/// Cargo. Ate o Marco 0 era texto livre no funcionario, e isso ja bastava para cadastrar.
/// Vira entidade agora porque o Marco 4 pergunta "quantos VIGILANTES preciso neste turno?"
/// — e essa pergunta nao tem resposta em cima de texto livre, onde "Vigilante",
/// "vigilante" e "Vigilante " sao tres cargos diferentes.
///
/// <see cref="NormalizedName"/> existe para essa unicidade: e o nome dobrado (minusculas,
/// sem acento, sem pontuacao) e tem indice unico por tenant. Dobrar no C# e nao no banco
/// mantem a regra igual na importacao, na tela e na API.
/// </summary>
public sealed class Position : ITenantOwned
{
    private Position() { }

    public PositionId Id { get; private set; }
    public Guid TenantId { get; set; }

    /// <summary>Como o cliente escreve, com acento e caixa. E o que aparece na tela.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Chave de comparacao. Nunca exibida.</summary>
    public string NormalizedName { get; private set; } = null!;

    public static Result<Position> Create(string? name)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length < 2)
            return Result.Failure<Position>(new Error(
                "position.name_required", "Informe o nome do cargo.", nameof(Name)));

        return Result.Success(new Position
        {
            Id = PositionId.New(),
            Name = name,
            NormalizedName = Normalize(name),
        });
    }

    public Result Rename(string? name)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length < 2)
            return Result.Failure("position.name_required", "Informe o nome do cargo.", nameof(Name));

        Name = name;
        NormalizedName = Normalize(name);
        return Result.Success();
    }

    /// <summary>
    /// Mesma dobra de acento explicita do leitor de planilha, e pelo mesmo motivo: nao pode
    /// depender de ICU nem de collation do banco. Ver EmployeeCsvParser.
    /// </summary>
    public static string Normalize(string? valor)
    {
        const string comAcento = "áàâãäéèêëíìîïóòôõöúùûüçñ";
        const string semAcento = "aaaaaeeeeiiiiooooouuuucn";

        var destino = new System.Text.StringBuilder(valor?.Length ?? 0);
        var espacoPendente = false;

        foreach (var caractere in (valor ?? string.Empty).ToLowerInvariant())
        {
            var indice = comAcento.IndexOf(caractere, StringComparison.Ordinal);
            var letra = indice >= 0 ? semAcento[indice] : caractere;

            if (char.IsLetterOrDigit(letra))
            {
                if (espacoPendente && destino.Length > 0)
                    destino.Append(' ');

                espacoPendente = false;
                destino.Append(letra);
            }
            else
            {
                espacoPendente = true;
            }
        }

        return destino.ToString();
    }
}

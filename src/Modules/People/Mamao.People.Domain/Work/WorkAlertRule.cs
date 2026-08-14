using System.Globalization;
using Mamao.People.Domain.Availability;

namespace Mamao.People.Domain.Work;

/// <summary>
/// O que o cartão avisa sem ninguém perguntar.
/// </summary>
public enum WorkAlertKind
{
    /// <summary>O prazo já passou.</summary>
    Atrasada,

    /// <summary>O responsável está fora hoje.</summary>
    ResponsavelAusente,

    /// <summary>O responsável sai antes do prazo — ainda dá para replanejar.</summary>
    ResponsavelSai,

    /// <summary>Ninguém está com ela.</summary>
    SemResponsavel,
}

public sealed record WorkAlert(WorkAlertKind Kind, string Message);

/// <summary>
/// A regra que faz este quadro valer mais que um quadro genérico: o cartão é lido junto
/// com a disponibilidade de quem responde por ele. "O Marcos entra de férias na quarta e o
/// prazo é sexta" é a frase que o gestor precisa ler ANTES de sexta — e é a frase que
/// nenhuma ferramenta de tarefas consegue dizer, porque ela não sabe quem está de férias.
///
/// Mora no domínio, e não no serviço, porque é regra de produto: precisa ser testável sem
/// banco e precisa doer no compilador quando mudar.
/// </summary>
public static class WorkAlertRule
{
    /// <summary>
    /// Avalia UM alerta — o mais grave.
    ///
    /// Um cartão cabe uma linha. Listar três avisos no mesmo cartão faz o gestor parar de
    /// ler os três, e o pior deles é justamente o que precisava ser lido.
    /// </summary>
    /// <param name="responsavelNome">Nome de quem responde, para o texto. Nulo vira "O responsável".</param>
    /// <param name="ausencias">Ocupações da pessoa que tocam o intervalo entre hoje e o prazo.</param>
    public static WorkAlert? Avaliar(
        WorkItem item,
        DateOnly hoje,
        string? responsavelNome,
        IEnumerable<Occupancy> ausencias)
    {
        // Concluída e cancelada não avisam nada: o alerta é sobre o que ainda vai acontecer.
        if (!item.Aberta)
            return null;

        if (item.DueOn is { } prazo && prazo < hoje)
        {
            var dias = hoje.DayNumber - prazo.DayNumber;
            return new WorkAlert(
                WorkAlertKind.Atrasada,
                dias == 1 ? "Venceu ontem." : $"Atrasada há {dias} dias.");
        }

        if (item.AssigneeId is null)
            return new WorkAlert(WorkAlertKind.SemResponsavel, "Sem responsável.");

        var nome = PrimeiroNome(responsavelNome);
        var blocos = ausencias as IReadOnlyCollection<Occupancy> ?? [.. ausencias];

        // Fora HOJE é mais grave que "vai sair": o trabalho já não está andando.
        var agora = blocos
            .Where(o => o.Cobre(hoje))
            .OrderByDescending(o => o.EndsOn)
            .FirstOrDefault();

        if (agora is not null)
        {
            var motivo = OccupancyKindLabels.Of(agora.Kind);

            // A distinção que muda a decisão do gestor: se a volta é depois do prazo, não
            // adianta esperar — ou muda o responsável, ou muda o prazo.
            var texto = item.DueOn is { } p && agora.EndsOn >= p
                ? $"{nome} está de {motivo} até {Dia(agora.EndsOn)} — depois do prazo ({Dia(p)})."
                : $"{nome} está de {motivo}, volta em {Dia(agora.EndsOn.AddDays(1))}.";

            return new WorkAlert(WorkAlertKind.ResponsavelAusente, texto);
        }

        if (item.DueOn is { } prazoFinal)
        {
            var sai = blocos
                .Where(o => o.StartsOn > hoje && o.StartsOn <= prazoFinal)
                .OrderBy(o => o.StartsOn)
                .FirstOrDefault();

            if (sai is not null)
            {
                return new WorkAlert(
                    WorkAlertKind.ResponsavelSai,
                    $"{nome} entra de {OccupancyKindLabels.Of(sai.Kind)} em {Dia(sai.StartsOn)}, antes do prazo ({Dia(prazoFinal)}).");
            }
        }

        return null;
    }

    private static string Dia(DateOnly d) => d.ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>"Ana Paula Souza" vira "Ana". Cartão é espaço curto, e o time se chama pelo primeiro nome.</summary>
    private static string PrimeiroNome(string? nomeCompleto)
    {
        if (string.IsNullOrWhiteSpace(nomeCompleto))
            return "O responsável";

        var espaco = nomeCompleto.Trim().IndexOf(' ');
        return espaco > 0 ? nomeCompleto.Trim()[..espaco] : nomeCompleto.Trim();
    }
}

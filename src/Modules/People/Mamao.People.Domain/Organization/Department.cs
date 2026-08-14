using Mamao.People.Contracts;
using Mamao.SharedKernel.Results;
using Mamao.SharedKernel.Tenancy;

namespace Mamao.People.Domain.Organization;

/// <summary>
/// Setor. Arvore: "Operacoes" tem "Turno A" e "Turno B" embaixo.
///
/// A arvore e guardada por <see cref="ParentId"/> MAIS um <see cref="Path"/> materializado
/// ("/id-raiz/id-filho/"). O caminho materializado existe porque a pergunta que o produto
/// faz o tempo todo e "todo mundo abaixo de Operacoes" — com ele isso e um LIKE com indice
/// de prefixo, em uma consulta; sem ele, seria recursao ou N+1.
///
/// Nao usamos `ltree`: e uma extensao a instalar e manter num banco cujo papel de
/// aplicacao e deliberadamente sem privilegio, e a arvore aqui tem dezenas de nos numa
/// empresa de 15 a 60 pessoas. Texto resolve, e da para ler direto no banco.
/// Ver docs/produto/modelo-de-dominio.md#people.
/// </summary>
public sealed class Department : ITenantOwned
{
    /// <summary>Teto de profundidade. Existe para barrar ciclo e arvore acidental, nao por limite tecnico.</summary>
    public const int MaxDepth = 6;

    private Department() { }

    public DepartmentId Id { get; private set; }
    public Guid TenantId { get; set; }

    public string Name { get; private set; } = null!;
    public DepartmentId? ParentId { get; private set; }

    /// <summary>Caminho materializado, sempre com barra no inicio e no fim: "/a/b/".</summary>
    public string Path { get; private set; } = null!;

    /// <summary>0 para raiz. Derivado do caminho, guardado para ordenar e indentar sem recalcular.</summary>
    public int Depth { get; private set; }

    /// <summary>Responsavel pelo setor. Opcional: nem toda empresa nomeia gestor de cada setor.</summary>
    public EmployeeId? ManagerId { get; private set; }

    public static Result<Department> Create(string? name, Department? parent)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length < 2)
            return Result.Failure<Department>(new Error(
                "department.name_required", "Informe o nome do setor.", nameof(Name)));

        if (parent is not null && parent.Depth + 1 >= MaxDepth)
        {
            return Result.Failure<Department>(new Error(
                "department.too_deep",
                $"Setor aninhado demais (limite de {MaxDepth} níveis). Simplifique a estrutura.",
                nameof(ParentId)));
        }

        var id = DepartmentId.New();

        return Result.Success(new Department
        {
            Id = id,
            Name = name,
            ParentId = parent?.Id,
            Path = $"{parent?.Path ?? "/"}{id.Value}/",
            Depth = parent is null ? 0 : parent.Depth + 1,
        });
    }

    public Result Rename(string? name)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length < 2)
            return Result.Failure("department.name_required", "Informe o nome do setor.", nameof(Name));

        Name = name;
        return Result.Success();
    }

    public void AssignManager(EmployeeId? managerId) => ManagerId = managerId;

    /// <summary>
    /// Move o setor para baixo de outro pai. Quem chama e responsavel por reescrever o
    /// caminho dos DESCENDENTES — o agregado nao os enxerga, e carregar a subarvore inteira
    /// para uma operacao rara seria pagar caro o tempo todo. Ver <see cref="Reparent"/>.
    /// </summary>
    public Result MoveTo(Department? novoPai)
    {
        if (novoPai is not null)
        {
            if (novoPai.Id == Id)
                return Result.Failure("department.cycle", "Um setor não pode ser pai de si mesmo.");

            // O caminho do novo pai conter este id significa que ele esta ABAIXO deste
            // setor: mover geraria um ciclo e um pedaco da arvore ficaria orfao.
            if (novoPai.Path.Contains($"/{Id.Value}/", StringComparison.Ordinal))
                return Result.Failure("department.cycle", "Não dá para mover um setor para dentro dele mesmo.");

            if (novoPai.Depth + 1 >= MaxDepth)
                return Result.Failure("department.too_deep", $"Setor aninhado demais (limite de {MaxDepth} níveis).");
        }

        Reparent(novoPai?.Id, $"{novoPai?.Path ?? "/"}{Id.Value}/", novoPai is null ? 0 : novoPai.Depth + 1);
        return Result.Success();
    }

    /// <summary>Reescreve pai, caminho e profundidade. Usado no move e na correcao dos descendentes.</summary>
    public void Reparent(DepartmentId? parentId, string path, int depth)
    {
        ParentId = parentId;
        Path = path;
        Depth = depth;
    }

    /// <summary>Prefixo que casa com este setor e tudo abaixo dele.</summary>
    public string SubtreePrefix => Path;

    /// <summary>
    /// Profundidade lida do proprio caminho: "/a/" tem 2 barras e e raiz, "/a/b/" tem 3 e
    /// e nivel 1. Derivar disso e sempre correto; recalcular por aritmetica de diferenca
    /// entre profundidades era o caminho para a arvore mentir depois de um move.
    /// </summary>
    public static int DepthFromPath(string path) => path.Count(c => c == '/') - 2;
}

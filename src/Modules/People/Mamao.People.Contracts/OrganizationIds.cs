using Mamao.SharedKernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mamao.People.Contracts;

/// <summary>
/// Identificador de setor. Mesmo motivo do <see cref="EmployeeId"/>: trocar um id de setor
/// por um de cargo numa chamada e o tipo de erro que Guid nu nao pega e teste raramente
/// cobre.
/// </summary>
[JsonConverter(typeof(DepartmentIdJsonConverter))]
public readonly record struct DepartmentId(Guid Value) : IStronglyTypedId
{
    public static DepartmentId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static DepartmentId Parse(string value) => new(Guid.Parse(value));
}

public sealed class DepartmentIdJsonConverter : JsonConverter<DepartmentId>
{
    public override DepartmentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, DepartmentId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>Identificador de cargo.</summary>
[JsonConverter(typeof(PositionIdJsonConverter))]
public readonly record struct PositionId(Guid Value) : IStronglyTypedId
{
    public static PositionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static PositionId Parse(string value) => new(Guid.Parse(value));
}

public sealed class PositionIdJsonConverter : JsonConverter<PositionId>
{
    public override PositionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, PositionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>Identificador de restricao — quem nao entra em certa atividade.</summary>
[JsonConverter(typeof(RestrictionIdJsonConverter))]
public readonly record struct RestrictionId(Guid Value) : IStronglyTypedId
{
    public static RestrictionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static RestrictionId Parse(string value) => new(Guid.Parse(value));
}

public sealed class RestrictionIdJsonConverter : JsonConverter<RestrictionId>
{
    public override RestrictionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, RestrictionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mamao.People.Contracts;

/// <summary>
/// Identificador de funcionario. Tipado para que <c>EmployeeId</c> e <c>DepartmentId</c>
/// nao possam ser trocados por engano numa chamada — erro que um Guid nu nao pega.
/// Serializa como string simples no JSON, nao como objeto.
/// </summary>
[JsonConverter(typeof(EmployeeIdJsonConverter))]
public readonly record struct EmployeeId(Guid Value)
{
    public static EmployeeId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static EmployeeId Parse(string value) => new(Guid.Parse(value));
}

public sealed class EmployeeIdJsonConverter : JsonConverter<EmployeeId>
{
    public override EmployeeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, EmployeeId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

using Mamao.SharedKernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mamao.People.Contracts;

/// <summary>Identificador de demanda — uma tarefa com responsavel e prazo.</summary>
[JsonConverter(typeof(WorkItemIdJsonConverter))]
public readonly record struct WorkItemId(Guid Value) : IStronglyTypedId
{
    public static WorkItemId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static WorkItemId Parse(string value) => new(Guid.Parse(value));
}

public sealed class WorkItemIdJsonConverter : JsonConverter<WorkItemId>
{
    public override WorkItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, WorkItemId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>Identificador de documento.</summary>
[JsonConverter(typeof(DocumentIdJsonConverter))]
public readonly record struct DocumentId(Guid Value) : IStronglyTypedId
{
    public static DocumentId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static DocumentId Parse(string value) => new(Guid.Parse(value));
}

public sealed class DocumentIdJsonConverter : JsonConverter<DocumentId>
{
    public override DocumentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, DocumentId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

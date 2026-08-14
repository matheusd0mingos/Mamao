using Mamao.SharedKernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mamao.People.Contracts;

/// <summary>Identificador de ocupacao — um bloco de tempo em que a pessoa nao esta livre.</summary>
[JsonConverter(typeof(OccupancyIdJsonConverter))]
public readonly record struct OccupancyId(Guid Value) : IStronglyTypedId
{
    public static OccupancyId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static OccupancyId Parse(string value) => new(Guid.Parse(value));
}

public sealed class OccupancyIdJsonConverter : JsonConverter<OccupancyId>
{
    public override OccupancyId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, OccupancyId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// <summary>Identificador de solicitacao de ausencia.</summary>
[JsonConverter(typeof(AbsenceRequestIdJsonConverter))]
public readonly record struct AbsenceRequestId(Guid Value) : IStronglyTypedId
{
    public static AbsenceRequestId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
    public static AbsenceRequestId Parse(string value) => new(Guid.Parse(value));
}

public sealed class AbsenceRequestIdJsonConverter : JsonConverter<AbsenceRequestId>
{
    public override AbsenceRequestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, AbsenceRequestId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

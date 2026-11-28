using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 将 <see cref="TaskId"/> 序列化为经过转义的层次路径，形式为 JSON 字符串或属性名称。
/// </summary>
/// <remarks>
/// <see cref="TaskId"/> 在其 <see cref="JsonConverterAttribute"/> 中指定此转换器，因此基于反射的序列化和
/// 源生成的序列化无需注册即可使用它。空标识符 <see cref="TaskId.None"/> 表示为
/// 空字符串。
/// </remarks>
public sealed class TaskIdJsonConverter : JsonConverter<TaskId>
{
    /// <inheritdoc/>
    public override TaskId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
            && TaskId.TryParse(reader.GetString(), out var result))
        {
            return result;
        }

        throw new JsonException("A durable task identifier must be an escaped hierarchical path.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TaskId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }

    /// <inheritdoc/>
    public override TaskId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Read(ref reader, typeToConvert, options);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TaskId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.ToString());
    }
}

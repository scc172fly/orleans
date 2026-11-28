using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Orleans.Durable.Journaling.Json;

internal static class JsonTypeInfoHelpers
{
    public static JsonTypeInfo<T> GetTypeInfo<T>(JsonSerializerOptions? options)
    {
        options ??= GetDefaultOptions();
        try
        {
            var typeInfo = options.GetTypeInfo(typeof(T));
            if (typeInfo is JsonTypeInfo<T> typedTypeInfo)
            {
                return typedTypeInfo;
            }
        }
        catch (NotSupportedException exception)
        {
            throw CreateMissingMetadataException<T>(exception);
        }
        catch (InvalidOperationException exception) when (options.TypeInfoResolver is null)
        {
            throw CreateMissingMetadataException<T>(exception);
        }

        throw CreateMissingMetadataException<T>();
    }

    // 裁剪及 Native AOT 发布禁用 System.Text.Json 反射时，JsonSerializerOptions.Default 使用
    // 空类型信息解析器，因此元数据缺失表现为 NotSupportedException，并产生上述配置错误。
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JsonSerializerOptions.Default resolves no reflection-based metadata when System.Text.Json reflection is disabled.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JsonSerializerOptions.Default resolves no reflection-based metadata when System.Text.Json reflection is disabled.")]
    private static JsonSerializerOptions GetDefaultOptions() => JsonSerializerOptions.Default;

    private static InvalidOperationException CreateMissingMetadataException<T>(Exception? innerException = null)
    {
        var valueType = typeof(T);
        return new(
            $"JSON journaling requires System.Text.Json metadata for journaled payload type '{valueType.FullName}'. "
            + "Add the type to a source-generated JsonSerializerContext using [JsonSerializable(typeof(...))] and register the context "
            + $"with UseJsonJournalFormat(MyJournalJsonContext.Default) or Configure<{nameof(JsonJournalOptions)}>(options => "
            + $"options.{nameof(JsonJournalOptions.AddTypeInfoResolver)}(MyJournalJsonContext.Default)).",
            innerException);
    }
}

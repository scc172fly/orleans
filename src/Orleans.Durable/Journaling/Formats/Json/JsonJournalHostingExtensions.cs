using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Durable.Journaling.Json;

/// <summary>
/// 用于配置基于 JSON Lines 的日志持久化的选项。
/// </summary>
public sealed class JsonJournalOptions
{
    /// <summary>
    /// 获取或设置用于序列化的 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    /// <remarks>
    /// 持久化条目编解码器从此选项实例中解析写入日志的每种有效载荷类型的 <see cref="JsonTypeInfo{T}"/>
    /// 元数据。执行裁剪或使用 Native AOT 时，请配置
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> 或 <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>，
    /// 为记入日志的值、键及状态类型提供源生成的元数据。
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.General);

    /// <summary>
    /// 将源生成的 JSON 元数据添加到用于日志有效载荷值的序列化器选项。
    /// </summary>
    /// <param name="typeInfoResolver">解析器，通常为生成的 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 实例。</param>
    /// <returns>用于链式调用的此选项实例。</returns>
    /// <remarks>
    /// 执行裁剪或使用 Native AOT 时，使用此方法为每种记入日志的键、值及状态类型注册元数据。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddJournaling().UseJsonJournalFormat(options =>
    /// {
    ///     options.AddTypeInfoResolver(MyJournalJsonContext.Default);
    /// });
    /// </code>
    /// </example>
    public JsonJournalOptions AddTypeInfoResolver(IJsonTypeInfoResolver typeInfoResolver)
    {
        ArgumentNullException.ThrowIfNull(typeInfoResolver);

        SerializerOptions.TypeInfoResolverChain.Add(typeInfoResolver);
        return this;
    }
}

/// <summary>
/// 用于配置 JSON Lines Orleans.Durable.Journaling 格式的扩展方法。
/// </summary>
public static class JsonJournalHostingExtensions
{
    /// <summary>
    /// 为此 Silo 配置 JSON Lines Orleans.Durable.Journaling 格式系列。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configure">用于配置 <see cref="JsonJournalOptions"/> 的可选委托。</param>
    /// <returns>用于链式调用的 Silo 构建器。</returns>
    /// <example>
    /// <code>
    /// builder.AddJournaling().UseJsonJournalFormat();
    ///
    /// builder.AddJournaling().UseJsonJournalFormat(MyJournalJsonContext.Default);
    ///
    /// builder.AddJournaling().UseJsonJournalFormat(options =>
    /// {
    ///     options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    ///     options.AddTypeInfoResolver(MyJournalJsonContext.Default);
    /// });
    /// </code>
    /// </example>
    public static ISiloBuilder UseJsonJournalFormat(this ISiloBuilder builder, Action<JsonJournalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddJsonJournalFormat(tryAdd: false);
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        return builder;
    }

    /// <summary>
    /// 为此 Silo 配置 JSON Lines Orleans.Durable.Journaling 格式系列，并为日志有效载荷值注册源生成的元数据。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="typeInfoResolver">解析器，通常为生成的 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 实例。</param>
    /// <returns>用于链式调用的 Silo 构建器。</returns>
    /// <remarks>
    /// 推荐此重载作为裁剪及 Native AOT 的便捷入口。提供的解析器
    /// 必须包含每种记入日志的键、值及状态类型。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddJournaling().UseJsonJournalFormat(MyJournalJsonContext.Default);
    /// </code>
    /// </example>
    public static ISiloBuilder UseJsonJournalFormat(this ISiloBuilder builder, IJsonTypeInfoResolver typeInfoResolver)
    {
        ArgumentNullException.ThrowIfNull(typeInfoResolver);

        return builder.UseJsonJournalFormat(options => options.AddTypeInfoResolver(typeInfoResolver));
    }

    internal static IServiceCollection AddJsonJournalFormat(this IServiceCollection services, bool tryAdd)
    {
        ArgumentNullException.ThrowIfNull(services);

        var key = JournalFormatServices.ValidateJournalFormatKey(JsonLinesJournalFormat.JournalFormatKey);
        services.AddOptions<JsonJournalOptions>();
        if (tryAdd)
        {
            services.TryAddSingleton<JsonLinesJournalFormat>();
            services.TryAddKeyedSingleton<IJournalFormat>(key, static (sp, _) => sp.GetRequiredService<JsonLinesJournalFormat>());
            services.TryAddSingleton<IJournalFormat>(static sp => sp.GetRequiredService<JsonLinesJournalFormat>());

            services.TryAddKeyedSingleton(typeof(IDurableDictionaryCommandCodec<,>), key, typeof(JsonDurableDictionaryCommandCodecService<,>));
            services.TryAddKeyedSingleton(typeof(IDurableListCommandCodec<>), key, typeof(JsonDurableListCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IDurableQueueCommandCodec<>), key, typeof(JsonDurableQueueCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IDurableSetCommandCodec<>), key, typeof(JsonDurableSetCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IDurableLogCommandCodec<>), key, typeof(JsonDurableLogCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IDurableValueCommandCodec<>), key, typeof(JsonDurableValueCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IPersistentStateCommandCodec<>), key, typeof(JsonPersistentStateCommandCodecService<>));
            services.TryAddKeyedSingleton(typeof(IDurableTaskCompletionSourceCommandCodec<>), key, typeof(JsonDurableTaskCompletionSourceCommandCodecService<>));
        }
        else
        {
            services.AddSingleton<JsonLinesJournalFormat>();
            services.AddKeyedSingleton<IJournalFormat>(key, static (sp, _) => sp.GetRequiredService<JsonLinesJournalFormat>());
            services.AddSingleton<IJournalFormat>(static sp => sp.GetRequiredService<JsonLinesJournalFormat>());

            services.AddKeyedSingleton(typeof(IDurableDictionaryCommandCodec<,>), key, typeof(JsonDurableDictionaryCommandCodecService<,>));
            services.AddKeyedSingleton(typeof(IDurableListCommandCodec<>), key, typeof(JsonDurableListCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IDurableQueueCommandCodec<>), key, typeof(JsonDurableQueueCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IDurableSetCommandCodec<>), key, typeof(JsonDurableSetCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IDurableLogCommandCodec<>), key, typeof(JsonDurableLogCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IDurableValueCommandCodec<>), key, typeof(JsonDurableValueCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IPersistentStateCommandCodec<>), key, typeof(JsonPersistentStateCommandCodecService<>));
            services.AddKeyedSingleton(typeof(IDurableTaskCompletionSourceCommandCodec<>), key, typeof(JsonDurableTaskCompletionSourceCommandCodecService<>));
        }

        return services;
    }
}

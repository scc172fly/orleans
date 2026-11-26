using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orleans.Durable.Journaling.Json;

/// <summary>
/// 配置 JSON 行基日记的选项。
/// </summary>
public sealed class JsonJournalOptions
{
    /// <summary>
    /// 获取或设置序列化所用的 <see cref="JsonSerializerOptions"/> 。
    /// </summary>
    /// <remarks>
    /// 耐用输入编码解码器解析 <see cref="JsonTypeInfo{T}"/>
    /// 在此选项实例中, 每个日记有效载荷类型的元数据。 配置
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/> 或 <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>
    /// 带有源生成元数据, 用于计算或使用本地 AOT 时的日记值、 密钥和状态类型。
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.General);

    /// <summary>
    /// 将源生成的元数据 JSON 添加到用于日记有效载荷值的序列器选项中。
    /// </summary>
    /// <param name="typeInfoResolver">解答器,通常是生成的 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 实例。</param>
    /// <returns>此链条选项实例。</returns>
    /// <remarks>
    /// 使用此方法来登记每个日记密钥、值和状态类型在剪切或使用本地自动输出时的元数据。
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
/// 配置 JSON 行 Orleans.Durable.Journaling 格式的扩展方法。
/// </summary>
public static class JsonJournalHostingExtensions
{
    /// <summary>
    /// 配置此配置 silo 和 JSON 行线 Orleans.Durable.Journaling 格式化家庭。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configure">配置 <see cref="JsonJournalOptions"/>的可选代表。</param>
    /// <returns>链条的 silo 构建器。</returns>
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
    /// 配置 silo ,使用 JSON 行 Orleans.Durable.Journaling 格式的家族式,并登记源生成元数据,以显示日记有效载荷值。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="typeInfoResolver">解答器,通常是生成的 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 实例。</param>
    /// <returns>链条的 silo 构建器。</returns>
    /// <remarks>
    /// 超载是推荐的用于剪切和本地 AOT 的低阻力切入点。 提供解答器
    /// 必须包含每个日记密钥、 值和状态类型 。
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

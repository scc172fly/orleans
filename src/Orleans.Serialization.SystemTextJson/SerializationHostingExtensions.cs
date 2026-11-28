using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities.Internal;
using System;
using System.Text.Json;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="ISerializerBuilder"/> 的扩展方法。
/// </summary>
public static class SerializationHostingExtensions
{
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(JsonCodec), typeof(JsonCodec));

    /// <summary>
    /// 添加使用 <see cref="JsonSerializer"/> 序列化及反序列化值的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSupported">用于指示此编解码器应序列化和复制哪些类型的委托。</param>
    /// <param name="jsonSerializerOptions">JSON 序列化器选项。</param>
    public static ISerializerBuilder AddJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSupported,
        JsonSerializerOptions? jsonSerializerOptions = null)
        => serializerBuilder.AddJsonSerializer(
            isSupported,
            isSupported,
            optionsBuilder => optionsBuilder.Configure(options =>
            {
                if (jsonSerializerOptions is not null)
                {
                    options.SerializerOptions = jsonSerializerOptions;
                }
            }));

    /// <summary>
    /// 添加使用 <see cref="JsonSerializer"/> 序列化及反序列化值的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSerializable">用于指示此编解码器应序列化哪些类型的委托。</param>
    /// <param name="isCopyable">用于指示此编解码器应复制哪些类型的委托。</param>
    /// <param name="configureOptions">用于配置 JSON 序列化器选项的委托。</param>
    public static ISerializerBuilder AddJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSerializable,
        Func<Type, bool>? isCopyable,
        Action<OptionsBuilder<JsonCodecOptions>>? configureOptions = null)
    {
        if (serializerBuilder is null) throw new ArgumentNullException(nameof(serializerBuilder));

        var services = serializerBuilder.Services;
        if (configureOptions != null)
        {
            configureOptions(services.AddOptions<JsonCodecOptions>());
        }

        if (isSerializable != null)
        {
            services.AddSingleton<ICodecSelector>(new DelegateCodecSelector
            {
                CodecName = JsonCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isSerializable
            });
        }

        if (isCopyable != null)
        {
            services.AddSingleton<ICopierSelector>(new DelegateCopierSelector
            {
                CopierName = JsonCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isCopyable
            });
        }

        if (!services.Contains(ServiceDescriptor))
        {
            services.AddSingleton<JsonCodec>();
            services.AddFromExisting<IGeneralizedCodec, JsonCodec>();
            services.AddFromExisting<IGeneralizedCopier, JsonCodec>();
            services.AddFromExisting<ITypeFilter, JsonCodec>();
            serializerBuilder.Configure(options => options.WellKnownTypeAliases[JsonCodec.WellKnownAlias] = typeof(JsonCodec));
        }

        return serializerBuilder;
    }
}

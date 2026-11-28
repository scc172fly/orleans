using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities.Internal;
using System;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="ISerializerBuilder"/> 的扩展方法。
/// </summary>
public static class SerializationHostingExtensions
{
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(NewtonsoftJsonCodec), typeof(NewtonsoftJsonCodec));

    /// <summary>
    /// 添加使用 <see cref="JsonSerializer"/> 序列化及反序列化值的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSupported">用于指示此编解码器应序列化和复制哪些类型的委托。</param>
    /// <param name="jsonSerializerSettings">JSON 序列化器设置。</param>
    public static ISerializerBuilder AddNewtonsoftJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSupported,
        JsonSerializerSettings? jsonSerializerSettings = null)
        => serializerBuilder.AddNewtonsoftJsonSerializer(
            isSupported,
            optionsBuilder => optionsBuilder.Configure(options =>
            {
                if (jsonSerializerSettings is not null)
                {
                    options.SerializerSettings = jsonSerializerSettings;
                }
            }));

    /// <summary>
    /// 添加使用 <see cref="JsonSerializer"/> 序列化及反序列化值的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSupported">用于指示此编解码器应序列化和复制哪些类型的委托。</param>
    /// <param name="configureOptions">用于配置 JSON 序列化器选项的委托。</param>
    public static ISerializerBuilder AddNewtonsoftJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSupported,
        Action<OptionsBuilder<NewtonsoftJsonCodecOptions>>? configureOptions)
        => serializerBuilder.AddNewtonsoftJsonSerializer(
            isSupported,
            isSupported,
            configureOptions);

    /// <summary>
    /// 添加使用 <see cref="JsonSerializer"/> 序列化及反序列化值的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSerializable">用于指示此编解码器应序列化哪些类型的委托。</param>
    /// <param name="isCopyable">用于指示此编解码器应复制哪些类型的委托。</param>
    /// <param name="configureOptions">用于配置 JSON 序列化器选项的委托。</param>
    public static ISerializerBuilder AddNewtonsoftJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSerializable,
        Func<Type, bool>? isCopyable,
        Action<OptionsBuilder<NewtonsoftJsonCodecOptions>>? configureOptions)
    {
        if (serializerBuilder is null) throw new ArgumentNullException(nameof(serializerBuilder));

        var services = serializerBuilder.Services;
        if (configureOptions != null)
        {
            configureOptions(services.AddOptions<NewtonsoftJsonCodecOptions>());
        }

        if (isSerializable != null)
        {
            services.AddSingleton<ICodecSelector>(new DelegateCodecSelector
            {
                CodecName = NewtonsoftJsonCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isSerializable
            });
        }

        if (isCopyable != null)
        {
            services.AddSingleton<ICopierSelector>(new DelegateCopierSelector
            {
                CopierName = NewtonsoftJsonCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isCopyable
            });
        }

        if (!services.Contains(ServiceDescriptor))
        {
            services.AddSingleton<NewtonsoftJsonCodec>();
            services.AddFromExisting<IGeneralizedCodec, NewtonsoftJsonCodec>();
            services.AddFromExisting<IGeneralizedCopier, NewtonsoftJsonCodec>();
            services.AddFromExisting<ITypeFilter, NewtonsoftJsonCodec>();
            serializerBuilder.Configure(options => options.WellKnownTypeAliases[NewtonsoftJsonCodec.WellKnownAlias] = typeof(NewtonsoftJsonCodec));
        }

        return serializerBuilder;
    }
}

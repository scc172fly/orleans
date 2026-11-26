using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities.Internal;
using System;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="ISerializerBuilder"/>的扩展方法。
/// </summary>
public static class SerializationHostingExtensions
{
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(NewtonsoftJsonCodec), typeof(NewtonsoftJsonCodec));

    /// <summary>
    /// 添加支持,以便使用 <see cref="JsonSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSupported">一位代表曾指出哪些类型应当按序号编号并抄录本编码器。</param>
    /// <param name="jsonSerializerSettings"> JSON 序列器设置。</param>
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
    /// 添加支持,以便使用 <see cref="JsonSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSupported">一位代表曾指出哪些类型应当按序号编号并抄录本编码器。</param>
    /// <param name="configureOptions">代表用于配置 JSON 序列器的选项。</param>
    public static ISerializerBuilder AddNewtonsoftJsonSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSupported,
        Action<OptionsBuilder<NewtonsoftJsonCodecOptions>>? configureOptions)
        => serializerBuilder.AddNewtonsoftJsonSerializer(
            isSupported,
            isSupported,
            configureOptions);

    /// <summary>
    /// 添加支持,以便使用 <see cref="JsonSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSerializable">代表用来表示该编码器应按哪些类型进行序列。</param>
    /// <param name="isCopyable">一位代表曾指出该编码器应复制哪类代码。</param>
    /// <param name="configureOptions">代表用于配置 JSON 序列器的选项。</param>
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

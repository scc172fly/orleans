using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities.Internal;
using System;
using System.Text.Json;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="ISerializerBuilder"/>的扩展方法。
/// </summary>
public static class SerializationHostingExtensions
{
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(JsonCodec), typeof(JsonCodec));

    /// <summary>
    /// 添加支持,以便使用 <see cref="JsonSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSupported">一位代表曾指出哪些类型应当按序号编号并抄录本编码器。</param>
    /// <param name="jsonSerializerOptions"> JSON 序列器选项。</param>
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
    /// 添加支持,以便使用 <see cref="JsonSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSerializable">代表用来表示该编码器应按哪些类型进行序列。</param>
    /// <param name="isCopyable">一位代表曾指出该编码器应复制哪类代码。</param>
    /// <param name="configureOptions">代表用于配置 JSON 序列器的选项。</param>
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

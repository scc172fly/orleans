using System;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Utilities.Internal;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="ISerializerBuilder"/>的扩展方法。
/// </summary>
public static class SerializationHostingExtensions
{
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(MessagePackCodec), typeof(MessagePackCodec));

    /// <summary>
    /// 添加支持,以便使用 <see cref="MessagePackSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSerializable">代表用来表示该编码器应按哪些类型进行序列。</param>
    /// <param name="isCopyable">一位代表曾指出该编码器应复制哪类代码。</param>
    /// <param name="messagePackSerializerOptions"> MessagePack 序列器选项。</param>
    public static ISerializerBuilder AddMessagePackSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSerializable = null,
        Func<Type, bool>? isCopyable = null,
        MessagePackSerializerOptions? messagePackSerializerOptions = null)
    {
        return serializerBuilder.AddMessagePackSerializer(
            isSerializable,
            isCopyable,
            optionsBuilder => optionsBuilder.Configure(options =>
            {
                if (messagePackSerializerOptions is not null)
                {
                    options.SerializerOptions = messagePackSerializerOptions;
                }
            })
        );
    }

    /// <summary>
    /// 添加支持,以便使用 <see cref="MessagePackSerializer"/>进行序列化和去序化。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSerializable">代表用来表示该编码器应按哪些类型进行序列。</param>
    /// <param name="isCopyable">一位代表曾指出该编码器应复制哪类代码。</param>
    /// <param name="configureOptions">用于配置 MessagePack 编码器选项的代表。</param>
    public static ISerializerBuilder AddMessagePackSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool>? isSerializable,
        Func<Type, bool>? isCopyable,
        Action<OptionsBuilder<MessagePackCodecOptions>>? configureOptions = null)
    {
        if (serializerBuilder is null) throw new ArgumentNullException(nameof(serializerBuilder));

        var services = serializerBuilder.Services;
        if (configureOptions != null)
        {
            configureOptions(services.AddOptions<MessagePackCodecOptions>());
        }

        if (isSerializable != null)
        {
            services.AddSingleton<ICodecSelector>(new DelegateCodecSelector
            {
                CodecName = MessagePackCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isSerializable
            });
        }

        if (isCopyable != null)
        {
            services.AddSingleton<ICopierSelector>(new DelegateCopierSelector
            {
                CopierName = MessagePackCodec.WellKnownAlias,
                IsSupportedTypeDelegate = isCopyable
            });
        }

        if (!services.Contains(ServiceDescriptor))
        {
            services.AddSingleton<MessagePackCodec>();
            services.AddFromExisting<IGeneralizedCodec, MessagePackCodec>();
            services.AddFromExisting<IGeneralizedCopier, MessagePackCodec>();
            services.AddFromExisting<ITypeFilter, MessagePackCodec>();
            serializerBuilder.Configure(options => options.WellKnownTypeAliases[MessagePackCodec.WellKnownAlias] = typeof(MessagePackCodec));
        }

        return serializerBuilder;
    }
}

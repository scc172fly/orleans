using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ProtobufCodec), typeof(ProtobufCodec));

    /// <summary>
    /// 添加支持,以便使用 <see cref="MessageParser"/>对方案IMessage类型进行序列整理和取消其序列处理。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    public static ISerializerBuilder AddProtobufSerializer(
        this ISerializerBuilder serializerBuilder)
        => serializerBuilder.AddProtobufSerializer(
            isSerializable: type => typeof(IMessage).IsAssignableFrom(type),
            isCopyable: type => typeof(IMessage).IsAssignableFrom(type));

    /// <summary>
    /// 添加支持,以便使用 <see cref="MessageParser"/>对方案IMessage类型进行序列整理和取消其序列处理。
    /// </summary>
    /// <param name="serializerBuilder">序列器的构建器。</param>
    /// <param name="isSerializable">代表用来表示该编码器应按哪些类型进行序列。</param>
    /// <param name="isCopyable">一位代表曾指出该编码器应复制哪类代码。</param>
    public static ISerializerBuilder AddProtobufSerializer(
        this ISerializerBuilder serializerBuilder,
        Func<Type, bool> isSerializable,
        Func<Type, bool> isCopyable)
    {
        if (serializerBuilder is null) throw new ArgumentNullException(nameof(serializerBuilder));
        if (isSerializable is null) throw new ArgumentNullException(nameof(isSerializable));
        if (isCopyable is null) throw new ArgumentNullException(nameof(isCopyable));

        var services = serializerBuilder.Services;

        services.AddSingleton<ICodecSelector>(new DelegateCodecSelector
        {
            CodecName = ProtobufCodec.WellKnownAlias,
            IsSupportedTypeDelegate = isSerializable
        });

        services.AddSingleton<ICopierSelector>(new DelegateCopierSelector
        {
            CopierName = ProtobufCodec.WellKnownAlias,
            IsSupportedTypeDelegate = isCopyable
        });

        if (!services.Contains(ServiceDescriptor))
        {
            services.AddSingleton<ProtobufCodec>();
            services.AddFromExisting<IGeneralizedCodec, ProtobufCodec>();
            services.AddFromExisting<IGeneralizedCopier, ProtobufCodec>();
            services.AddFromExisting<ITypeFilter, ProtobufCodec>();

            serializerBuilder.Configure(options => options.WellKnownTypeAliases[ProtobufCodec.WellKnownAlias] = typeof(ProtobufCodec));
        }

        return serializerBuilder;
    }
}

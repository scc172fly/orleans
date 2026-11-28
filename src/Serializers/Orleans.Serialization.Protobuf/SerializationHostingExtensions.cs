using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly ServiceDescriptor ServiceDescriptor = new(typeof(ProtobufCodec), typeof(ProtobufCodec));

    /// <summary>
    /// 添加使用 <see cref="MessageParser"/> 对 Protobuf IMessage 类型进行序列化和反序列化的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    public static ISerializerBuilder AddProtobufSerializer(
        this ISerializerBuilder serializerBuilder)
        => serializerBuilder.AddProtobufSerializer(
            isSerializable: type => typeof(IMessage).IsAssignableFrom(type),
            isCopyable: type => typeof(IMessage).IsAssignableFrom(type));

    /// <summary>
    /// 添加使用 <see cref="MessageParser"/> 对 Protobuf IMessage 类型进行序列化和反序列化的支持。
    /// </summary>
    /// <param name="serializerBuilder">序列化构建器。</param>
    /// <param name="isSerializable">用于指示此编解码器应序列化哪些类型的委托。</param>
    /// <param name="isCopyable">用于指示此编解码器应复制哪些类型的委托。</param>
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

using System;
using System.Runtime.Serialization;
using MessagePack;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="MessagePackCodec"/> 的选项。
/// </summary>
public class MessagePackCodecOptions
{
    /// <summary>
    /// 获取或设置 <see cref="MessagePackSerializerOptions"/>。
    /// </summary>
    public MessagePackSerializerOptions SerializerOptions { get; set; } = MessagePackSerializerOptions.Standard;

    /// <summary>
    /// 获取或设置一个标志，指示 MessagePackSerializer 是否允许使用带有 <see cref="DataContractAttribute"/> 标记的契约。
    /// </summary>
    public bool AllowDataContractAttributes { get; set; }

    /// <summary>
    /// 获取或设置一个委托，用于确定 MessagePack 序列化器是否支持某类型的序列化和反序列化。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置一个委托，用于确定 MessagePack 序列化器是否支持某类型的复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

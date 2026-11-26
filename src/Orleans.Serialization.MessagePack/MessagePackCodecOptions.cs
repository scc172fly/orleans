using System;
using System.Runtime.Serialization;
using MessagePack;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="MessagePackCodec"/>选项。
/// </summary>
public class MessagePackCodecOptions
{
    /// <summary>
    /// 获取或设置 <see cref="MessagePackSerializerOptions"/>。
    /// </summary>
    public MessagePackSerializerOptions SerializerOptions { get; set; } = MessagePackSerializerOptions.Standard;

    /// <summary>
    /// 设置或设置允许使用 <see cref="DataContractAttribute"/> 标志性合同的 MessagePackSerializer标志。
    /// </summary>
    public bool AllowDataContractAttributes { get; set; }

    /// <summary>
    /// 获取或设置代表用来确定某一类型是否得到 MessagePack 序列器的佐证,以便进行序列和消毒。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置用于确定某一类型是否得到 MessagePack 序列器支持的代表复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

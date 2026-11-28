using MsgPack.Serialization;

namespace Presence.Grains.Models;

/// <summary>
/// 此类封装 HeartbeatData 的序列化和反序列化。
/// 仅用于模拟实际场景中设备以二进制形式传入数据的情况。
/// 如果直接将 HeartbeatData 实例作为 Grain 调用参数传递，则不需要此序列化器，
/// 因为 Orleans 会为所有参数类型自动生成高效的序列化器。
/// </summary>
public static class HeartbeatDataDotNetSerializer
{
    private static readonly MessagePackSerializer<HeartbeatData> Serializer =
        MessagePackSerializer.Get<HeartbeatData>();

    public static byte[] Serialize(HeartbeatData item)
    {
        using var stream = new MemoryStream();
        Serializer.Pack(stream, item);
        return stream.ToArray();
    }

    public static HeartbeatData Deserialize(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Serializer.Unpack(stream);
    }
}

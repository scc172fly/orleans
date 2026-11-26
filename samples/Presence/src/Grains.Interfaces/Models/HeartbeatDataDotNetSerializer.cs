using MsgPack.Serialization;

namespace Presence.Grains.Models;

/// <summary>
/// 本类封装 HeartbeatData的序列/分离。
/// 它仅用于模拟数据来自二进制设备的实际生活情景。
/// 如果将 HeartbeatData 实例作为理由转至 grain 呼叫,则不需要这种序列器
/// 因为 Orleans 对所有参数类型自动生成高效的序列器。
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

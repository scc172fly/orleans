using System;
using System.Text.Json;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="JsonCodec"/>选项。
/// </summary>
public class JsonCodecOptions
{
    /// <summary>
    /// 获取或设置 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new();

    /// <summary>
    /// 获取或设置 <see cref="JsonReaderOptions"/>。
    /// </summary>
    public JsonReaderOptions ReaderOptions { get; set; }

    /// <summary>
    /// 获取或设置 <see cref="JsonWriterOptions"/>。
    /// </summary>
    public JsonWriterOptions WriterOptions { get; set; }

    /// <summary>
    /// 获取或设置代表用来确定某一类型是否得到 JSON 序列器的佐证,以便进行序列和消毒。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置用于确定某一类型是否得到 JSON 序列器支持的代表复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

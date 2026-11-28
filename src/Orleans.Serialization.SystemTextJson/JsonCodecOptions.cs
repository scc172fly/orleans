using System;
using System.Text.Json;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="JsonCodec"/> 的选项。
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
    /// 获取或设置一个委托，用于确定 JSON 序列化器是否支持某类型的序列化和反序列化。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置一个委托，用于确定 JSON 序列化器是否支持某类型的复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

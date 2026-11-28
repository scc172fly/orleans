using Newtonsoft.Json;
using System;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="NewtonsoftJsonCodec"/> 的选项。
/// </summary>
public class NewtonsoftJsonCodecOptions
{
    /// <summary>
    /// 获取或设置 <see cref="JsonSerializerSettings"/>。
    /// </summary>
    public JsonSerializerSettings? SerializerSettings { get; set; }

    /// <summary>
    /// 获取或设置一个委托，用于确定 JSON 序列化器是否支持某类型的序列化和反序列化。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置一个委托，用于确定 JSON 序列化器是否支持某类型的复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

using Newtonsoft.Json;
using System;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="NewtonsoftJsonCodec"/>选项。
/// </summary>
public class NewtonsoftJsonCodecOptions
{
    /// <summary>
    /// 获取或设置 <see cref="JsonSerializerSettings"/>。
    /// </summary>
    public JsonSerializerSettings? SerializerSettings { get; set; }

    /// <summary>
    /// 获取或设置代表用来确定某一类型是否得到 JSON 序列器的佐证,以便进行序列和消毒。
    /// </summary>
    public Func<Type, bool?>? IsSerializableType { get; set; }

    /// <summary>
    /// 获取或设置用于确定某一类型是否得到 JSON 序列器支持的代表复制。
    /// </summary>
    public Func<Type, bool?>? IsCopyableType { get; set; }
}

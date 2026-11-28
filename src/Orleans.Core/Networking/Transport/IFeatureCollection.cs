using System;
using System.Collections.Generic;

namespace Orleans.Connections.Transport;

/// <summary>
/// 表示带类型的功能集合。
/// </summary>
public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>>
{
    /// <summary>
    /// 指示集合是否可修改。
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// 每次修改时递增，可用于验证缓存结果。
    /// </summary>
    int Revision { get; }

    /// <summary>
    /// 获取或设置指定功能。将值设为 null 会移除该功能。
    /// </summary>
    /// <param name="key"></param>
    /// <returns>请求的功能；如果不存在，则为 null。</returns>
    object? this[Type key] { get; set; }

    /// <summary>
    /// 从集合中检索请求的功能。
    /// </summary>
    /// <typeparam name="TFeature">功能键。</typeparam>
    /// <returns>请求的功能；如果不存在，则为 null。</returns>
    TFeature? Get<TFeature>();

    /// <summary>
    /// 在集合中设置指定功能。
    /// </summary>
    /// <typeparam name="TFeature">功能键。</typeparam>
    /// <param name="instance">功能值。</param>
    void Set<TFeature>(TFeature? instance);
}

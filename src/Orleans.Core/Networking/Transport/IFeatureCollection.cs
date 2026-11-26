using System;
using System.Collections.Generic;

namespace Orleans.Connections.Transport;

/// <summary>
/// 代表一组已打印的特征。
/// </summary>
public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>>
{
    /// <summary>
    /// 表示收藏是否可以修改。
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// 每次修改都加添,可用于核实隐藏的结果。
    /// </summary>
    int Revision { get; }

    /// <summary>
    /// 获取或设置指定特性。设置空值将删除该特性。
    /// </summary>
    /// <param name="key"></param>
    /// <returns>请求的特性, 如果没有, 则无效 。</returns>
    object? this[Type key] { get; set; }

    /// <summary>
    /// 从收藏中获取请求的特性 。
    /// </summary>
    /// <typeparam name="TFeature">特性密钥。</typeparam>
    /// <returns>请求的特性, 如果没有, 则无效 。</returns>
    TFeature? Get<TFeature>();

    /// <summary>
    /// 设置收藏中指定特性。
    /// </summary>
    /// <typeparam name="TFeature">特性密钥。</typeparam>
    /// <param name="instance">特性值。</param>
    void Set<TFeature>(TFeature? instance);
}

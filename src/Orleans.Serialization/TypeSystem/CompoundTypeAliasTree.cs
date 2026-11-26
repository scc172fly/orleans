
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 表示复合类别名为前缀树。
/// </summary>
public class CompoundTypeAliasTree
{
    private Dictionary<object, CompoundTypeAliasTree>? _children;

    /// <summary>
    /// 初始化 <see cref="CompoundTypeAliasTree"/> 类的新实例。
    /// </summary>
    private CompoundTypeAliasTree(object? key, Type? value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// 获得此节点的钥匙 。
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// 获取此节点的值 。
    /// </summary>
    public Type? Value { get; private set; }

    /// <summary>
    /// 创建没有密钥或值的根节点新树。
    /// </summary>
    public static CompoundTypeAliasTree Create() => new(default, default);

    internal CompoundTypeAliasTree? GetChildOrDefault(object key)
    {
        TryGetChild(key, out var result);
        return result;
    }

    internal bool TryGetChild(object key, out CompoundTypeAliasTree? result)
    {
        if (_children is { } children)
        {
            return children.TryGetValue(key, out result);
        }

        result = default;
        return false;
    }

    /// <summary>
    /// 添加树上的节点。
    /// </summary>
    /// <param name="key">新节点的密钥。</param>
    public CompoundTypeAliasTree Add(Type key) => AddInternal(key);

    /// <summary>
    /// 添加树上的节点。
    /// </summary>
    /// <param name="key">新节点的密钥。</param>
    public CompoundTypeAliasTree Add(string key) => AddInternal(key);

    /// <summary>
    /// 添加树上的节点。
    /// </summary>
    /// <param name="key">新节点的密钥。</param>
    /// <param name="value">新节点的值。</param>
    public CompoundTypeAliasTree Add(string key, Type value) => AddInternal(key, value);

    /// <summary>
    /// 获取或创建中间节点,同时保留其注册类型值。
    /// </summary>
    /// <param name="key">组件类型。</param>
    /// <returns>现有或创建的节点。</returns>
    public CompoundTypeAliasTree GetOrAdd(Type key) => GetChildOrDefault(key) ?? AddInternal(key);

    /// <summary>
    /// 获取或创建中间节点,同时保留其注册类型值。
    /// </summary>
    /// <param name="key">组件名称。</param>
    /// <returns>现有或创建的节点。</returns>
    public CompoundTypeAliasTree GetOrAdd(string key) => GetChildOrDefault(key) ?? AddInternal(key);

    /// <summary>
    /// 添加树上的节点。
    /// </summary>
    /// <param name="key">新节点的密钥。</param>
    /// <param name="value">新节点的值。</param>
    public CompoundTypeAliasTree Add(Type key, Type value) => AddInternal(key, value);

    private CompoundTypeAliasTree AddInternal(object key) => AddInternal(key, default);
    private CompoundTypeAliasTree AddInternal(object key, Type? value)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(key, nameof(key));
#else
        if (key is null) throw new ArgumentNullException(nameof(key));
#endif
        _children ??= new();

        if (_children.TryGetValue(key, out var existing))
        {
            if (value is not null && existing.Value is { } type && type != value)
            {
                // 当同一个 grain 接口在多个没有交叉引用的组件中被使用时,
                // 代码- gen 将生成两个代码的代码, 因为它独立运作, 但是在启动时它们会合并。

                // 在这种情况下,如果钥匙存在,其价值与所添加的价值相同,
                // 并且由于逻辑上是一样的,我们可以只归还现有的 CompoundTypeAliasTree。

                // 本案中第一个可以获胜。
                return existing;
            }

            existing.Value = value;
            return existing;
        }
        else
        {
            return _children[key] = new CompoundTypeAliasTree(key, value);
        }
    }
}

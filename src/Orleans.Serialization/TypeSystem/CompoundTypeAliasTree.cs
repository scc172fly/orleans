
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 以一棵前缀树表示复合类型别名。
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
    /// 获取此节点的键。
    /// </summary>
    public object? Key { get; }

    /// <summary>
    /// 获取此节点的值。
    /// </summary>
    public Type? Value { get; private set; }

    /// <summary>
    /// 创建一棵新树，其根节点没有键或值。
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
    /// 向树中添加节点。
    /// </summary>
    /// <param name="key">新节点的键。</param>
    public CompoundTypeAliasTree Add(Type key) => AddInternal(key);

    /// <summary>
    /// 向树中添加节点。
    /// </summary>
    /// <param name="key">新节点的键。</param>
    public CompoundTypeAliasTree Add(string key) => AddInternal(key);

    /// <summary>
    /// 向树中添加节点。
    /// </summary>
    /// <param name="key">新节点的键。</param>
    /// <param name="value">新节点的值。</param>
    public CompoundTypeAliasTree Add(string key, Type value) => AddInternal(key, value);

    /// <summary>
    /// 获取或创建中间节点，同时保留其已注册的类型值。
    /// </summary>
    /// <param name="key">组成部分的类型。</param>
    /// <returns>已有节点或新建节点。</returns>
    public CompoundTypeAliasTree GetOrAdd(Type key) => GetChildOrDefault(key) ?? AddInternal(key);

    /// <summary>
    /// 获取或创建中间节点，同时保留其已注册的类型值。
    /// </summary>
    /// <param name="key">组成部分的名称。</param>
    /// <returns>已有节点或新建节点。</returns>
    public CompoundTypeAliasTree GetOrAdd(string key) => GetChildOrDefault(key) ?? AddInternal(key);

    /// <summary>
    /// 向树中添加节点。
    /// </summary>
    /// <param name="key">新节点的键。</param>
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
                // 当多个没有相互引用的程序集使用同一个 Grain 接口时，
                // 代码生成会各自独立进行，因此会为各程序集生成代码，但这些代码在启动时会合并。

                // 在这种情况下，如果键已存在且其值与要添加的值相同，
                // 由于两者在逻辑上相同，可以直接返回现有的 CompoundTypeAliasTree。

                // 在这种情况下，以第一个注册项为准。
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

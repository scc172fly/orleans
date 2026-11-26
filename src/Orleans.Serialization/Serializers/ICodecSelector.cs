using System;

namespace Orleans.Serialization.Serializers;

/// <summary>
/// 普通用途编码器(例如 JSON 编码器)的功能允许选择使用这些编码器的类型。
/// </summary>
public interface ICodecSelector
{
    /// <summary>
    /// 众所周知的编码名称,用来匹配一个实例和一个编码。
    /// </summary>
    public string CodecName { get; }

    /// <summary>
    /// 如果指定代码用于此类型, 返回为真 。
    /// </summary>
    /// <param name="type">检查的类型。</param>
    /// <returns><see langword="true"/> 如果编码器应用于指定类型;否则, <see langword="false"/>。</returns>
    public bool IsSupportedType(Type type);
}

/// <summary>
/// 通用复印机(如 JSON 复印机)使用功能,允许选择使用这些复印机的类型。
/// </summary>
public interface ICopierSelector
{
    /// <summary>
    /// 众所周知的复印机名,用来比对一个案件和一台复印机。
    /// </summary>
    public string CopierName { get; }

    /// <summary>
    /// 如果指定复印机用于此类型, 返回为真 。
    /// </summary>
    /// <param name="type">检查的类型。</param>
    /// <returns><see langword="true"/> 如果复印机应用于指定类型;否则, <see langword="false"/>。</returns>
    public bool IsSupportedType(Type type);
}

/// <summary>
/// 使用代表的 <see cref="ICodecSelector"/> 实施。
/// </summary>
public sealed class DelegateCodecSelector : ICodecSelector
{
    /// <inheritdoc/>
    public string CodecName { get; init; } = null!;

    /// <summary>
    /// 获取或初始化用于确定是否支持某类型的代表。
    /// </summary>
    public Func<Type, bool> IsSupportedTypeDelegate { get; init; } = null!;

    /// <inheritdoc/>
    public bool IsSupportedType(Type type) => IsSupportedTypeDelegate(type);
}

/// <summary>
/// 使用代表的 <see cref="ICopierSelector"/> 实施。
/// </summary>
public sealed class DelegateCopierSelector : ICopierSelector
{
    /// <inheritdoc/>
    public string CopierName { get; init; } = null!;

    /// <summary>
    /// 获取或初始化用于确定是否支持某类型的代表。
    /// </summary>
    public Func<Type, bool> IsSupportedTypeDelegate { get; init; } = null!;

    /// <inheritdoc/>
    public bool IsSupportedType(Type type) => IsSupportedTypeDelegate(type);
}

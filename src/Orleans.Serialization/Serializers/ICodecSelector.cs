using System;

namespace Orleans.Serialization.Serializers;

/// <summary>
/// 通用编解码器（例如 JSON 编解码器）所使用的功能，允许类型选择使用这些编解码器。
/// </summary>
public interface ICodecSelector
{
    /// <summary>
    /// 已知的编解码器名称，用于匹配实例和编解码器。
    /// </summary>
    public string CodecName { get; }

    /// <summary>
    /// 如果此类型应使用指定编解码器，则返回 true。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns><see langword="true"/>，如果指定类型应使用该编解码器；否则为 <see langword="false"/>。</returns>
    public bool IsSupportedType(Type type);
}

/// <summary>
/// 通用复制器（例如 JSON 复制器）所使用的功能，允许类型选择使用这些复制器。
/// </summary>
public interface ICopierSelector
{
    /// <summary>
    /// 已知的复制器名称，用于匹配实例和复制器。
    /// </summary>
    public string CopierName { get; }

    /// <summary>
    /// 如果此类型应使用指定复制器，则返回 true。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns><see langword="true"/>，如果指定类型应使用该复制器；否则为 <see langword="false"/>。</returns>
    public bool IsSupportedType(Type type);
}

/// <summary>
/// 使用委托的 <see cref="ICodecSelector"/> 实现。
/// </summary>
public sealed class DelegateCodecSelector : ICodecSelector
{
    /// <inheritdoc/>
    public string CodecName { get; init; } = null!;

    /// <summary>
    /// 获取或初始化用于确定是否支持某个类型的委托。
    /// </summary>
    public Func<Type, bool> IsSupportedTypeDelegate { get; init; } = null!;

    /// <inheritdoc/>
    public bool IsSupportedType(Type type) => IsSupportedTypeDelegate(type);
}

/// <summary>
/// 使用委托的 <see cref="ICopierSelector"/> 实现。
/// </summary>
public sealed class DelegateCopierSelector : ICopierSelector
{
    /// <inheritdoc/>
    public string CopierName { get; init; } = null!;

    /// <summary>
    /// 获取或初始化用于确定是否支持某个类型的委托。
    /// </summary>
    public Func<Type, bool> IsSupportedTypeDelegate { get; init; } = null!;

    /// <inheritdoc/>
    public bool IsSupportedType(Type type) => IsSupportedTypeDelegate(type);
}

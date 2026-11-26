namespace Orleans.Serialization.Invocation;

/// <summary>
/// 表示标有招揽目标以及目标扩展对象。
/// </summary>
public interface ITargetHolder
{
    /// <summary>
    /// 获得目标实例。
    /// </summary>
    /// <returns>目标。</returns>
    object? GetTarget();

    /// <summary>
    /// 获取指定类型部件的组件。
    /// </summary>
    /// <param name="componentType">组件类型。</param>
    /// <returns>带有指定类型的组成部分。</returns>
    object? GetComponent(Type componentType);
}

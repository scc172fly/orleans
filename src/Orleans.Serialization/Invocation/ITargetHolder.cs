namespace Orleans.Serialization.Invocation;

/// <summary>
/// 表示同时持有调用目标及目标扩展的对象。
/// </summary>
public interface ITargetHolder
{
    /// <summary>
    /// 获取目标实例。
    /// </summary>
    /// <returns>目标。</returns>
    object? GetTarget();

    /// <summary>
    /// 获取指定类型的组件。
    /// </summary>
    /// <param name="componentType">组件类型。</param>
    /// <returns>具有指定类型的组件。</returns>
    object? GetComponent(Type componentType);
}

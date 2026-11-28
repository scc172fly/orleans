using Orleans.Serialization.Invocation;
namespace Orleans.Runtime;

/// <summary>
/// <see cref="ITargetHolder"/> 的扩展方法。
/// </summary>
public static class TargetHolderExtensions
{
    /// <summary>
    /// 获取指定类型的组件。
    /// </summary>
    /// <typeparam name="TComponent">组件类型。</typeparam>
    /// <param name="targetHolder">从中获取组件的目标持有器。</param>
    /// <returns>具有指定类型的组件。</returns>
    public static TComponent? GetComponent<TComponent>(this ITargetHolder targetHolder) where TComponent : class => targetHolder.GetComponent(typeof(TComponent)) as TComponent;
}

using Orleans.Serialization.Invocation;
namespace Orleans.Runtime;

/// <summary>
/// <see cref="ITargetHolder"/>的推广方法。
/// </summary>
public static class TargetHolderExtensions
{
    /// <summary>
    /// 获取指定类型部件的组件。
    /// </summary>
    /// <typeparam name="TComponent">组件类型。</typeparam>
    /// <param name="targetHolder">目标控制者从中取回部件。</param>
    /// <returns>带有指定类型的组成部分。</returns>
    public static TComponent? GetComponent<TComponent>(this ITargetHolder targetHolder) where TComponent : class => targetHolder.GetComponent(typeof(TComponent)) as TComponent;
}

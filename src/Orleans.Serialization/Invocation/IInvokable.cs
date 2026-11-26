using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 表示可非同步引用的对象。
    /// </summary>
    public interface IInvokable : IDisposable
    {
        /// <summary>
        /// 获得召回目标。
        /// </summary>
        /// <returns>呼叫目标。</returns>
        object? GetTarget();

        /// <summary>
        /// 设置从 <see cref="ITargetHolder"/>实例中选择的调用目标。
        /// </summary>
        /// <param name="holder">呼叫目标。</param>
        void SetTarget(ITargetHolder holder);

        /// <summary>
        /// 启动对象 。
        /// </summary>
        ValueTask<Response> Invoke();

        /// <summary>
        /// 获得参数数 。
        /// </summary>
        int GetArgumentCount();

        /// <summary>
        /// 获取指定索引的参数。
        /// </summary>
        /// <param name="index">引证索引。</param>
        /// <returns>指定索引的参数。</returns>
        object? GetArgument(int index);

        /// <summary>
        /// 将参数设置在指定的索引上。
        /// </summary>
        /// <param name="index">引证索引。</param>
        /// <param name="value">参数值</param>
        void SetArgument(int index, object value);

        /// <summary>
        /// 获得方法名称 。
        /// </summary>
        string GetMethodName();

        /// <summary>
        /// 获得完整的界面名称。
        /// </summary>
        string GetInterfaceName();

        /// <summary>
        /// 获取活动名称,即介面名称和方法名称。
        /// </summary>
        string GetActivityName();

        /// <summary>
        /// 获取方法信息对象,该对象可以是 <see langword="null"/>。
        /// </summary>
        MethodInfo GetMethod();

        /// <summary>
        /// 获得接口类型。
        /// </summary>
        Type GetInterfaceType();

        /// <summary>
        /// 获得默认反应超时 。
        /// </summary>
        TimeSpan? GetDefaultResponseTimeout() => default;

        /// <summary>
        /// 获取此请求的取消符。 如果请求不接受取消符, 则该标记为 <see cref="CancellationToken.None"/>。
        /// </summary>
        /// <returns>此请求的取消标记 。</returns>
        CancellationToken GetCancellationToken() => default;

        /// <summary>
        /// 尝试取消该援引。
        /// </summary>
        /// <remarks>只有在 <see cref="SetTarget(ITargetHolder)"/> 请求被发出,只对支持取消的请求产生影响。</remarks>
        /// <returns><see langword="true"/> 如果请求取消,否则 <see langword="false"/>。</returns>
        bool TryCancel() => false;

        /// <summary>
        /// <see langword="true"/> ,如果该请求支持取消; <see langword="false"/> ,则另行处理。
        /// </summary>
        bool IsCancellable => false;
    }
}

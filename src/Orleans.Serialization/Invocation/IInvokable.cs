using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 表示可异步调用的对象。
    /// </summary>
    public interface IInvokable : IDisposable
    {
        /// <summary>
        /// 获取调用目标。
        /// </summary>
        /// <returns>调用目标。</returns>
        object? GetTarget();

        /// <summary>
        /// 从 <see cref="ITargetHolder"/> 实例设置调用目标。
        /// </summary>
        /// <param name="holder">调用目标。</param>
        void SetTarget(ITargetHolder holder);

        /// <summary>
        /// 调用此对象。
        /// </summary>
        ValueTask<Response> Invoke();

        /// <summary>
        /// 获取实参数量。
        /// </summary>
        int GetArgumentCount();

        /// <summary>
        /// 获取指定索引处的实参。
        /// </summary>
        /// <param name="index">实参索引。</param>
        /// <returns>指定索引处的实参。</returns>
        object? GetArgument(int index);

        /// <summary>
        /// 设置指定索引处的实参。
        /// </summary>
        /// <param name="index">实参索引。</param>
        /// <param name="value">实参值。</param>
        void SetArgument(int index, object value);

        /// <summary>
        /// 获取方法名称。
        /// </summary>
        string GetMethodName();

        /// <summary>
        /// 获取接口的完整名称。
        /// </summary>
        string GetInterfaceName();

        /// <summary>
        /// 获取 Activity 名称，该名称包含接口名和方法名。
        /// </summary>
        string GetActivityName();

        /// <summary>
        /// 获取方法信息对象，其值可能为 <see langword="null"/>。
        /// </summary>
        MethodInfo GetMethod();

        /// <summary>
        /// 获取接口类型。
        /// </summary>
        Type GetInterfaceType();

        /// <summary>
        /// 获取默认响应超时时间。
        /// </summary>
        TimeSpan? GetDefaultResponseTimeout() => default;

        /// <summary>
        /// 获取此请求的取消令牌。如果请求不接受取消令牌，则为 <see cref="CancellationToken.None"/>。
        /// </summary>
        /// <returns>此请求的取消令牌。</returns>
        CancellationToken GetCancellationToken() => default;

        /// <summary>
        /// 尝试取消调用。
        /// </summary>
        /// <remarks>仅在调用 <see cref="SetTarget(ITargetHolder)"/> 之后才有效，并且只对支持取消的请求产生作用。</remarks>
        /// <returns><see langword="true"/>，如果已请求取消；否则为 <see langword="false"/>。</returns>
        bool TryCancel() => false;

        /// <summary>
        /// <see langword="true"/>，如果此请求支持取消；否则为 <see langword="false"/>。
        /// </summary>
        bool IsCancellable => false;
    }
}

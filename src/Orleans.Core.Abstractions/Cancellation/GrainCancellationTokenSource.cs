using System;
using System.Threading.Tasks;
using System.Threading;

namespace Orleans
{
    /// <summary>
    /// 类似于 <see cref="CancellationTokenSource"/>，但可以在 Grain 之间传递。
    /// </summary>
    [Obsolete("Use CancellationTokenSource instead.")]
    public sealed class GrainCancellationTokenSource : IDisposable
    {
        /// <summary>
        /// 底层 Grain 取消令牌。
        /// </summary>
        private readonly GrainCancellationToken _grainCancellationToken;

        /// <summary>
        /// 初始化 <see cref="T:Orleans.GrainCancellationTokenSource"/>。
        /// </summary>
        public GrainCancellationTokenSource()
        {
            _grainCancellationToken = new GrainCancellationToken(Guid.NewGuid());
        }

        /// <summary>
        /// 获取 <see cref="GrainCancellationTokenSource">CancellationToken</see>，
        /// 它与此 <see cref="GrainCancellationTokenSource"/> 关联。
        /// </summary>
        /// <value><see cref="GrainCancellationToken">CancellationToken</see>，
        /// 与此 <see cref="GrainCancellationToken"/> 关联。</value>
        public GrainCancellationToken Token
        {
            get { return _grainCancellationToken; }
        }

        /// <summary>
        /// 获取一个值，指示是否已请求取消。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此属性指示是否已请求取消此令牌源，例如调用其 <see cref="Cancel()" /> 方法后。
        /// </para>
        /// <para>
        /// 如果此属性返回 true，只能保证已经请求取消，并不保证对应令牌注册的所有处理程序均已执行完毕，也不保证
        /// 取消请求已传播到所有已注册的处理程序和远程目标。可能需要额外的同步，尤其是在相关对象
        /// 被并发取消的情况下。
        /// </para>
        /// </remarks>
        public bool IsCancellationRequested
        {
            get { return _grainCancellationToken.IsCancellationRequested; }
        }

        /// <summary>
        /// 发出取消请求。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 关联的 <see cref="GrainCancellationToken" /> 将收到取消通知，并转换到如下状态：
        /// <see cref="GrainCancellationToken.CancellationToken"><see cref="IsCancellationRequested"/></see> 返回 true。对于下述令牌，
        /// 在 <see cref="CancellationToken" /> 上注册的所有回调或可取消操作都将执行。
        /// </para>
        /// <para>
        /// 向令牌注册的可取消操作及回调不应 <see langword="throw"/> 异常。不过，<see cref="Cancel"/> 的此重载会将所有抛出的异常聚合为一个
        /// <see cref="AggregateException" />，使单个回调抛出异常也不会阻止其他已注册回调执行。
        /// </para>
        /// <para>调用回调时，将恢复注册每个回调时所捕获的 <see cref="System.Threading.ExecutionContext" />。</para>
        /// </remarks>
        /// <exception cref="AggregateException">聚合异常，包含关联的 <see cref="GrainCancellationToken" /> 上已注册回调抛出的所有异常。</exception>
        /// <exception cref="ObjectDisposedException">此 <see cref="GrainCancellationTokenSource" /> 已被释放。</exception>
        public Task Cancel()
        {
            return _grainCancellationToken.Cancel();
        }

        /// <summary>
        /// 释放此 <see cref="T:Orleans.Async.GrainCancellationTokenSource" /> 使用的资源。
        /// </summary>
        /// <remarks>
        /// 此方法与任何其他调用并发执行时都不是线程安全的。
        /// </remarks>
        public void Dispose()
        {
            _grainCancellationToken.Dispose();
        }
    }
}

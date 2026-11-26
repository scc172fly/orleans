using System;
using System.Threading.Tasks;
using System.Threading;

namespace Orleans
{
    /// <summary>
    /// 类比于 <see cref="CancellationTokenSource"/> ,可在 grains之间发送。
    /// </summary>
    [Obsolete("Use CancellationTokenSource instead.")]
    public sealed class GrainCancellationTokenSource : IDisposable
    {
        /// <summary>
        /// 后台 grain 取消符符号 。
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
        /// 获取 <see cref="GrainCancellationTokenSource">CancellationToken</see>
        /// 与此 <see cref="GrainCancellationTokenSource"/>相关联。
        /// </summary>
        /// <value>下 <see cref="GrainCancellationToken">CancellationToken</see>
        /// 与此 <see cref="GrainCancellationToken"/>相关联。</value>
        public GrainCancellationToken Token
        {
            get { return _grainCancellationToken; }
        }

        /// <summary>
        /// 获得一个值,显示是否请求取消。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 表示是否请求取消该象征性源,例如由于要求使用其 <see cref="Cancel()" /> 方法而请求取消。
        /// </para>
        /// <para>
        /// 如果该属性返回为真, 它只能保证请求取消。 它不能保证每个以相应牌子注册的操作员都已完成执行, 也不能保证
        /// 取消取消请求已完成向所有已登记处理者和远程目标的传播。 可能需要进一步同步化, 特别是在相关对象被撤销的情况下
        /// 同时。
        /// </para>
        /// </remarks>
        public bool IsCancellationRequested
        {
            get { return _grainCancellationToken.IsCancellationRequested; }
        }

        /// <summary>
        /// 传送取消请求书。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 将通知相关的 <see cref="GrainCancellationToken" /> 取消注销,并过渡至一个以下国家:
        /// <see cref="GrainCancellationToken.CancellationToken"><see cref="IsCancellationRequested"/>[</see> 返回为真实。在
        /// <see cref="CancellationToken" /> 将执行。
        /// </para>
        /// <para>
        /// 以象征性方式登记的可取消的操作和回回调不应有 <see langword="throw"/> 例外。然而,这种超载 <see cref="Cancel"/> 将汇总任何被投入
        /// <see cref="AggregateException" /> ,这样一次回击扔出例外不会阻止其他已登记的回击被执行。
        /// </para>
        /// <para>每一次回调登记时所捕捉的 <see cref="System.Threading.ExecutionContext" /> ,在回调被援引时将重新恢复。</para>
        /// </remarks>
        /// <exception cref="AggregateException">包含相关人员被登记的回调所造成所有例外的合并例外 <see cref="GrainCancellationToken" /> .</exception>
        /// <exception cref="ObjectDisposedException">今 <see cref="GrainCancellationTokenSource" /> 已经处理。</exception>
        public Task Cancel()
        {
            return _grainCancellationToken.Cancel();
        }

        /// <summary>
        /// 释放本 <see cref="T:Orleans.Async.GrainCancellationTokenSource" />所使用的资源。
        /// </summary>
        /// <remarks>
        /// 对于其他同时通话的电话,此方法不使用线索安全 。
        /// </remarks>
        public void Dispose()
        {
            _grainCancellationToken.Dispose();
        }
    }
}

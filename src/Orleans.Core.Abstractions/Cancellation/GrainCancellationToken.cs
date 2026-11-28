using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 类似于 <see cref="CancellationToken"/>，但可以在 Grain 之间传递。
    /// </summary>
    [Obsolete("Use CancellationToken instead.")]
    [Immutable]
    public sealed class GrainCancellationToken : IDisposable
    {
        /// <summary>
        /// 底层取消令牌源。
        /// </summary>
        [NonSerialized]
        private readonly CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 已接收此令牌的远程 Grain 的引用。
        /// </summary>
        [NonSerialized]
        private readonly ConcurrentDictionary<GrainId, GrainReference> _targetGrainReferences;

        /// <summary>
        /// 用于管理 Grain 取消令牌的运行时。
        /// </summary>
        [NonSerialized]
        private IGrainCancellationTokenRuntime? _cancellationTokenRuntime;

        /// <summary>
        /// 初始化 <see cref="T:Orleans.GrainCancellationToken"/>。
        /// </summary>
        /// <param name="id">
        /// 令牌标识符。
        /// </param>
        internal GrainCancellationToken(Guid id)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            Id = id;
            _targetGrainReferences = new ConcurrentDictionary<GrainId, GrainReference>();
        }

        /// <summary>
        /// 初始化 <see cref="T:Orleans.GrainCancellationToken"/>。
        /// </summary>
        /// <param name="id">
        /// 令牌标识符。
        /// </param>
        /// <param name="canceled">
        /// 此实例是否已经取消。
        /// </param>
        /// <param name="runtime">
        /// 运行时。
        /// </param>
        internal GrainCancellationToken(Guid id, bool canceled, IGrainCancellationTokenRuntime? runtime = null) : this(id)
        {
            _cancellationTokenRuntime = runtime;
            if (canceled)
            {
                // 调用 _cancellationTokenSource 的 Cancel 仅用于“保存”已取消状态。
                _cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// 获取令牌的唯一标识符。
        /// </summary>
        internal Guid Id { get; private set; }

        /// <summary>
        /// 获取底层取消令牌。
        /// </summary>
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        /// <summary>
        /// 获取一个值，指示是否已请求取消。
        /// </summary>
        internal bool IsCancellationRequested => _cancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// 取消该取消令牌。
        /// </summary>
        /// <returns>
        /// 表示该操作的 <see cref="Task"/>。
        /// </returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1849:Call async methods when in an async method",
            Justification = "Cancellation callbacks must execute synchronously on the current Orleans scheduler context before remote propagation.")]
        internal Task Cancel()
        {
            if (_cancellationTokenRuntime == null)
            {
                try
                {
                    _cancellationTokenSource.Cancel();
                    return Task.CompletedTask;
                }
                catch (Exception exception)
                {
                    return Task.FromException(exception);
                }
            }

            return _cancellationTokenRuntime.Cancel(Id, _cancellationTokenSource, _targetGrainReferences);
        }

        /// <summary>
        /// 使指定 Grain 引用订阅取消通知。
        /// </summary>
        /// <param name="runtime">Grain 取消机制的运行时。</param>
        /// <param name="grainReference">要添加的 Grain 引用。</param>
        internal void AddGrainReference(IGrainCancellationTokenRuntime runtime, GrainReference grainReference)
        {
            if (_cancellationTokenRuntime == null)
                _cancellationTokenRuntime = runtime;
            _targetGrainReferences.TryAdd(grainReference.GrainId, grainReference);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cancellationTokenSource.Dispose();
        }
    }
}

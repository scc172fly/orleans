using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 类比于 <see cref="CancellationToken"/> ,可在 grains之间发送。
    /// </summary>
    [Obsolete("Use CancellationToken instead.")]
    [Immutable]
    public sealed class GrainCancellationToken : IDisposable
    {
        /// <summary>
        /// 基本取消的象征性源。
        /// </summary>
        [NonSerialized]
        private readonly CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 此代号被传递到的远程 grains 引用。
        /// </summary>
        [NonSerialized]
        private readonly ConcurrentDictionary<GrainId, GrainReference> _targetGrainReferences;

        /// <summary>
        /// 管理 grain 取消待标的运行时间。
        /// </summary>
        [NonSerialized]
        private IGrainCancellationTokenRuntime? _cancellationTokenRuntime;

        /// <summary>
        /// 初始化 <see cref="T:Orleans.GrainCancellationToken"/>。
        /// </summary>
        /// <param name="id">
        /// 象征性id。
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
        /// 象征性id。
        /// </param>
        /// <param name="canceled">
        /// 是否已经取消此实例 。
        /// </param>
        /// <param name="runtime">
        /// 运行时间。
        /// </param>
        internal GrainCancellationToken(Guid id, bool canceled, IGrainCancellationTokenRuntime? runtime = null) : this(id)
        {
            _cancellationTokenRuntime = runtime;
            if (canceled)
            {
                // 我们取消取消的取消Token源 , 只需“储存”取消状态。
                _cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// 获取该代号的唯一代号
        /// </summary>
        internal Guid Id { get; private set; }

        /// <summary>
        /// 获取基本取消标记 。
        /// </summary>
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        /// <summary>
        /// 获得一个值, 显示是否请求取消 。
        /// </summary>
        internal bool IsCancellationRequested => _cancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// 取消取消标记。
        /// </summary>
        /// <returns>
        /// 代表行动的一个 <see cref="Task"/> 。
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
        /// 订阅所提供的注销通知 grain 参引。
        /// </summary>
        /// <param name="runtime">取消时间 grain 。</param>
        /// <param name="grainReference">下 grain 添加引用。</param>
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

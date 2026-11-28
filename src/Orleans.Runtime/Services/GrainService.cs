using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.ConsistentRing;
using Orleans.Runtime.Scheduler;
using Orleans.Services;

namespace Orleans.Runtime
{
    /// <summary>用于实现类 Grain 的分区服务的基类，其每个 Silo 的实例由 Silo 运行时自动实例化并启动。</summary>
    public abstract partial class GrainService : SystemTarget, IRingRangeListener, IGrainService
    {
        private readonly IConsistentRingProvider ring;
        private readonly string typeName;
        private GrainServiceStatus status;

        private readonly ILogger Logger;

        /// <summary>获取在停止 Grain 服务时发出取消信号的令牌。</summary>
        protected CancellationTokenSource StoppedCancellationTokenSource { get; }

        /// <summary>获取 Grain 服务实例拥有的环范围版本的单调递增序列号。</summary>
        protected int RangeSerialNumber { get; private set; }

        /// <summary>获取 Grain 服务实例当前拥有的分区环范围。</summary>
        protected IRingRange RingRange { get; private set; } = null!;

        /// <summary>获取 Grain 服务实例的状态。</summary>
        protected GrainServiceStatus Status
        {
            get { return status; }
            set
            {
                OnStatusChange(status, value);
                status = value;
            }
        }

        /// <summary>仅为满足反射要求。不要在你的实现中使用它。</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Do not call the empty constructor.")]
        protected GrainService() : base()
        {
            throw new Exception("This should not be constructed by client code.");
        }

        /// <summary>Grain 服务应使用的构造函数。</summary>
        internal GrainService(GrainId grainId, IConsistentRingProvider ringProvider, SystemTargetShared shared)
            : base(SystemTargetGrainId.Create(grainId.Type, shared.SiloAddress), shared)
        {
            typeName = this.GetType().FullName!;
            Logger = shared.LoggerFactory.CreateLogger(typeName);

            ring = ringProvider;
            StoppedCancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>Grain 服务应使用的构造函数。</summary>
        protected GrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory)
            : this(grainId, silo.RingProvider, silo.Services.GetRequiredService<SystemTargetShared>())
        {
        }

        /// <summary>
        /// 初始化服务时调用。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        public virtual Task Init(IServiceProvider serviceProvider)
        {
            return Task.CompletedTask;
        }

        private void OnStatusChange(GrainServiceStatus oldStatus, GrainServiceStatus newStatus)
        {
            if (oldStatus != GrainServiceStatus.Started && newStatus == GrainServiceStatus.Started)
            {
                SubscribeToRangeChangeEvents();
            }
            if (oldStatus != GrainServiceStatus.Stopped && newStatus == GrainServiceStatus.Stopped)
            {
                ring.UnSubscribeFromRangeChangeEvents(this);
            }
        }

        private protected void SubscribeToRangeChangeEvents() => ring.SubscribeToRangeChangeEvents(this);

        /// <summary>服务正在启动时调用。</summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        public virtual Task Start()
        {
            RingRange = ring.GetMyRange();
            LogInformationServiceStarting(Logger, this.typeName, Silo, new(Silo), RingRange);
            StartInBackground().Ignore();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 初始化的延迟部分，在服务已经启动后执行（以加快启动）。
        /// 将 Status 设置为 Started。
        /// </summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        protected virtual Task StartInBackground()
        {
            Status = GrainServiceStatus.Started;
            return Task.CompletedTask;
        }

        /// <summary>服务正在停止时调用。</summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        public virtual Task Stop()
        {
#pragma warning disable CA1849 // 派生服务的回调在调用方的此调度器上运行，并在状态转换之前完成。
            StoppedCancellationTokenSource.Cancel();
#pragma warning restore CA1849

            LogInformationServiceStopping(Logger, typeName);
            Status = GrainServiceStatus.Stopped;

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        void IRingRangeListener.RangeChangeNotification(IRingRange oldRange, IRingRange newRange, bool increased)
        {
            if (this is IGrainServiceRangeChangeQueue rangeChangeQueue)
            {
                rangeChangeQueue.QueueRangeChange(oldRange, newRange, increased);
            }
            else
            {
                this.WorkItemGroup.QueueTask(() => OnRangeChange(oldRange, newRange, increased), this).Ignore();
            }
        }

        /// <summary>
        /// 集群状态变化导致服务实例拥有的环范围发生变化时调用。
        /// </summary>
        /// <param name="oldRange">旧范围。</param>
        /// <param name="newRange">新范围。</param>
        /// <param name="increased">指示范围是否扩大的值。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        public virtual Task OnRangeChange(IRingRange oldRange, IRingRange newRange, bool increased)
        {
            LogInformationRangeChanged(Logger, oldRange, newRange, increased);
            RingRange = newRange;
            RangeSerialNumber++;

            return Task.CompletedTask;
        }

        private readonly struct SiloAddressHashCodeLogValue(SiloAddress silo)
        {
            public override string ToString() => silo.GetConsistentHashCode().ToString("X8");
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.RS_ServiceStarting,
            Message = "Starting {TypeName} grain service on: {Silo} x{HashCode}, with range {RingRange}"
        )]
        private static partial void LogInformationServiceStarting(ILogger logger, string typeName, SiloAddress silo, SiloAddressHashCodeLogValue hashCode, IRingRange ringRange);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.RS_ServiceStopping,
            Message = "Stopping {TypeName} grain service"
        )]
        private static partial void LogInformationServiceStopping(ILogger logger, string typeName);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.RS_RangeChanged,
            Message = "My range changed from {OldRange} to {NewRange} increased = {Increased}"
        )]
        private static partial void LogInformationRangeChanged(ILogger logger, IRingRange oldRange, IRingRange newRange, bool increased);

        /// <summary>Grain 服务可能的状态。</summary>
        protected enum GrainServiceStatus
        {
            /// <summary>正在初始化。</summary>
            Booting = 0,
            /// <summary>服务已成功启动。</summary>
            Started,
            /// <summary>服务已停止。</summary>
            Stopped,
        }
    }

    internal interface IGrainServiceRangeChangeQueue
    {
        void QueueRangeChange(IRingRange oldRange, IRingRange newRange, bool increased);
    }
}

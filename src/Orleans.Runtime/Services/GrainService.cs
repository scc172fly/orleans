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
    /// <summary>实施a grain- 类似分割的服务,每 silo 自动即时和开始于 silo 运行时间</summary>
    public abstract partial class GrainService : SystemTarget, IRingRangeListener, IGrainService
    {
        private readonly IConsistentRingProvider ring;
        private readonly string typeName;
        private GrainServiceStatus status;

        private readonly ILogger Logger;

        /// <summary>获取信号标记, 停止 grain 服务时发出取消的信号</summary>
        protected CancellationTokenSource StoppedCancellationTokenSource { get; }

        /// <summary>获得 grain 服务实例所拥有环区域版本单质增加的序列号</summary>
        protected int RangeSerialNumber { get; private set; }

        /// <summary>获取目前由 grain 服务实例</summary>拥有的分隔圈范围
        protected IRingRange RingRange { get; private set; } = null!;

        /// <summary>获得 grain 服务实例</summary>的地位
        protected GrainServiceStatus Status
        {
            get { return status; }
            set
            {
                OnStatusChange(status, value);
                status = value;
            }
        }

        /// <summary>只是为了让反射快乐。 不要在您的执行中使用它</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Do not call the empty constructor.")]
        protected GrainService() : base()
        {
            throw new Exception("This should not be constructed by client code.");
        }

        /// <summary> grain 服务</summary>使用建构器
        internal GrainService(GrainId grainId, IConsistentRingProvider ringProvider, SystemTargetShared shared)
            : base(SystemTargetGrainId.Create(grainId.Type, shared.SiloAddress), shared)
        {
            typeName = this.GetType().FullName!;
            Logger = shared.LoggerFactory.CreateLogger(typeName);

            ring = ringProvider;
            StoppedCancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary> grain 服务</summary>使用建构器
        protected GrainService(GrainId grainId, Silo silo, ILoggerFactory loggerFactory)
            : this(grainId, silo.RingProvider, silo.Services.GetRequiredService<SystemTargetShared>())
        {
        }

        /// <summary>
        /// 服务初始化时即启动
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
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

        /// <summary>服役开始时被调用</summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        public virtual Task Start()
        {
            RingRange = ring.GetMyRange();
            LogInformationServiceStarting(Logger, this.typeName, Silo, new(Silo), RingRange);
            StartInBackground().Ignore();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 服务开始后执行的初始化的延迟部分( 以加快启动) 。
        /// 设置要开始状态 。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        protected virtual Task StartInBackground()
        {
            Status = GrainServiceStatus.Started;
            return Task.CompletedTask;
        }

        /// <summary>停止服役时被传唤</summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        public virtual Task Stop()
        {
#pragma warning disable CA1849 // Derived-service callbacks run on this calling scheduler and complete before the status transition.
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
        /// 服务机构拥有的环域因集群状态变化而改变时,当该服务机构所属环域范围发生变化时,就被启动
        /// </summary>
        /// <param name="oldRange">旧区域。</param>
        /// <param name="newRange">新范围。</param>
        /// <param name="increased">表示范围是否扩大的值。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
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

        /// <summary>一种 grain 服务可能的状况</summary>
        protected enum GrainServiceStatus
        {
            /// <summary>初始化工作正在进行</summary>
            Booting = 0,
            /// <summary>服务已成功启动</summary>
            Started,
            /// <summary>服务已停止</summary>
            Stopped,
        }
    }

    internal interface IGrainServiceRangeChangeQueue
    {
        void QueueRangeChange(IRingRange oldRange, IRingRange newRange, bool increased);
    }
}

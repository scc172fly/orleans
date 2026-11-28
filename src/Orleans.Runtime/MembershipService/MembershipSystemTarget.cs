using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.MembershipService
{
    internal sealed partial class MembershipSystemTarget : SystemTarget, IMembershipService, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly IMembershipManager membershipManager;
        private readonly ILogger<MembershipSystemTarget> log;
        private readonly IInternalGrainFactory grainFactory;
        private readonly MessagingInstruments _messagingInstruments;
        private readonly TimeProvider _timeProvider;

        public MembershipSystemTarget(
            IMembershipManager membershipManager,
            ILogger<MembershipSystemTarget> log,
            IInternalGrainFactory grainFactory,
            MessagingInstruments messagingInstruments,
            SystemTargetShared shared,
            [FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider)
            : base(Constants.MembershipServiceType, shared)
        {
            this.membershipManager = membershipManager;
            this.log = log;
            this.grainFactory = grainFactory;
            _messagingInstruments = messagingInstruments;
            _timeProvider = timeProvider;
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        public Task Ping(int pingNumber, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task MembershipChangeNotification(
            MembershipTableSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Version != MembershipVersion.MinValue)
            {
                await this.membershipManager.ProcessGossipSnapshot(snapshot, cancellationToken);
            }
            else
            {
                LogTraceReceivedGossipMembershipChangeNotificationWithMinValue(this.log);
                await ReadTable(cancellationToken);
            }
        }

        /// <summary>
        /// 向远程 Silo 发送 ping。应从 <see cref="SiloHealthMonitor"/> 调用此方法，
        /// 以便从 <see cref="MembershipSystemTarget"/> 的上下文发起调用。
        /// </summary>
        /// <param name="remoteSilo">要 ping 的远程 Silo。</param>
        /// <param name="probeNumber">用于诊断的探测编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>向远程 Silo 发送 ping 的结果。</returns>
        public async Task ProbeRemoteSilo(SiloAddress remoteSilo, int probeNumber, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this.RunOrQueueTask(() => ProbeInternal(remoteSilo, probeNumber, cancellationToken));
        }

        /// <summary>
        /// 经中间 Silo 向远程 Silo 发送 ping。应从 <see cref="SiloHealthMonitor"/> 调用此方法，
        /// 以便从 <see cref="MembershipSystemTarget"/> 的上下文发起调用。
        /// </summary>
        /// <param name="intermediary">将直接探测目标的中间 Silo。</param>
        /// <param name="target">将被探测的目标。</param>
        /// <param name="probeTimeout">最终直接探测请求的超时时间。</param>
        /// <param name="probeNumber">用于诊断的探测编号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>向远程 Silo 发送 ping 的结果。</returns>
        public Task<IndirectProbeResponse> ProbeRemoteSiloIndirectly(SiloAddress intermediary, SiloAddress target, TimeSpan probeTimeout, int probeNumber, CancellationToken cancellationToken)
        {
            Task<IndirectProbeResponse> ProbeIndirectly(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var remoteOracle = this.grainFactory.GetSystemTarget<IMembershipService>(Constants.MembershipServiceType, intermediary);
                return remoteOracle.ProbeIndirectly(target, probeTimeout, probeNumber, token);
            }

            return this.RunOrQueueTask(ProbeIndirectly, cancellationToken);
        }

        public async Task<IndirectProbeResponse> ProbeIndirectly(
            SiloAddress target,
            TimeSpan probeTimeout,
            int probeNumber,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probeTimer = TimeProviderValueStopwatch.StartNew(_timeProvider);
            var succeeded = false;
            string? failureMessage = null;
            using var timeout = new CancellationTokenSource(probeTimeout, _timeProvider);
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                try
                {
                    await this.ProbeInternal(target, probeNumber, probeCancellation.Token);
                }
                catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var timeoutException = new TimeoutException($"The probe exceeded its timeout of {probeTimeout}.", exception);
                    LogWarningRequestedProbeTimeoutExceeded(this.log, timeoutException, probeTimeout);
                    throw timeoutException;
                }

                succeeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failureMessage = $"Encountered exception {LogFormatter.PrintException(exception)}";
            }

            var probeResponseTime = probeTimer.GetElapsedTime(out var probeEndTimestamp);
            var healthScore = this.ActivationServices.GetRequiredService<ILocalSiloHealthMonitor>()
                .GetLocalHealthStatus(probeTimer.StartTimestamp, probeEndTimestamp, LocalSiloHealthCheckCategory.Local)
                .Score;
            return new IndirectProbeResponse
            {
                Succeeded = succeeded,
                IntermediaryHealthScore = healthScore,
                FailureMessage = failureMessage,
                ProbeResponseTime = probeResponseTime,
            };
        }

        public async Task GossipToRemoteSilos(
            List<SiloAddress> gossipPartners,
            MembershipTableSnapshot snapshot,
            SiloAddress updatedSilo,
            SiloStatus updatedStatus,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            async Task Gossip()
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tasks = new List<Task>(gossipPartners.Count);
                foreach (var silo in gossipPartners)
                {
                    tasks.Add(this.GossipToRemoteSilo(silo, snapshot, updatedSilo, updatedStatus, cancellationToken));
                }

                await Task.WhenAll(tasks);
            }

            await this.RunOrQueueTask(Gossip);
        }

        private async Task GossipToRemoteSilo(
            SiloAddress silo,
            MembershipTableSnapshot snapshot,
            SiloAddress updatedSilo,
            SiloStatus updatedStatus,
            CancellationToken cancellationToken)
        {
            LogTraceSendingStatusUpdateGossipNotification(this.log, updatedSilo, updatedStatus, silo);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remoteOracle = this.grainFactory.GetSystemTarget<IMembershipService>(Constants.MembershipServiceType, silo);
                await remoteOracle.MembershipChangeNotification(snapshot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogWarningErrorSendingGossipNotificationToRemoteSilo(this.log, exception, silo);
            }
        }

        private async Task ReadTable(CancellationToken cancellationToken)
        {
            try
            {
                await this.membershipManager.Refresh(null, cancellationToken);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogErrorErrorRefreshingMembershipTable(this.log, exception);
            }
        }

        private Task ProbeInternal(SiloAddress remoteSilo, int probeNumber, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task task;
            try
            {
                RequestContext.Set(RequestContext.PING_APPLICATION_HEADER, true);
                var remoteOracle = this.grainFactory.GetSystemTarget<IMembershipService>(Constants.MembershipServiceType, remoteSilo);
                task = remoteOracle.Ping(probeNumber, cancellationToken);

                // 更新统计计数器。只统计成功发送的 Ping，但不要求已收到响应。
                _messagingInstruments.OnPingSend(remoteSilo);
            }
            finally
            {
                RequestContext.Remove(RequestContext.PING_APPLICATION_HEADER);
            }

            return task;
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle observer)
        {
            // 不执行任何操作，只确保启动时创建此实例。
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "-Received GOSSIP MembershipChangeNotification with MembershipVersion.MinValue. Going to read the table"
        )]
        private static partial void LogTraceReceivedGossipMembershipChangeNotificationWithMinValue(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Requested probe timeout {ProbeTimeout} exceeded"
        )]
        private static partial void LogWarningRequestedProbeTimeoutExceeded(ILogger logger, Exception exception, TimeSpan probeTimeout);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "-Sending status update GOSSIP notification about silo {UpdatedSilo}, status {UpdatedStatus}, to silo {RemoteSilo}"
        )]
        private static partial void LogTraceSendingStatusUpdateGossipNotification(ILogger logger, SiloAddress updatedSilo, SiloStatus updatedStatus, SiloAddress remoteSilo);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipGossipSendFailure,
            Level = LogLevel.Warning,
            Message = "Error sending gossip notification to remote silo '{Silo}'."
        )]
        private static partial void LogWarningErrorSendingGossipNotificationToRemoteSilo(ILogger logger, Exception exception, SiloAddress silo);

        [LoggerMessage(
            EventId = (int)ErrorCode.MembershipGossipProcessingFailure,
            Level = LogLevel.Error,
            Message = "Error refreshing membership table."
        )]
        private static partial void LogErrorErrorRefreshingMembershipTable(ILogger logger, Exception exception);
    }
}

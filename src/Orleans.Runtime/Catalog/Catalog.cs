using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.GrainDirectory;
using Orleans.Runtime.GrainDirectory;
using Orleans.Runtime.Messaging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Orleans.Diagnostics;

namespace Orleans.Runtime
{
    internal sealed partial class Catalog : SystemTarget, ICatalog, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly ActivationCollector activationCollector;
        private readonly ActivationDirectory activations;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger logger;
        private readonly GrainContextActivator grainActivator;
        private readonly CatalogInstruments _catalogInstruments;
        private readonly MessagingProcessingInstruments _messagingProcessingInstruments;
        private ISiloStatusOracle _siloStatusOracle = null!;

        // 使用锁带条纹来启动创建,以减少争议
        private const int LockCount = 32; // 一定是2的能量
        private const int LockMask = LockCount - 1;
        private readonly Lock[] _locks = new Lock[LockCount];

        public Catalog(
            ActivationDirectory activationDirectory,
            ActivationCollector activationCollector,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            GrainContextActivator grainActivator,
            CatalogInstruments catalogInstruments,
            MessagingProcessingInstruments messagingProcessingInstruments,
            SystemTargetShared shared)
            : base(Constants.CatalogType, shared)
        {
            this.activations = activationDirectory;
            this.serviceProvider = serviceProvider;
            this.grainActivator = grainActivator;
            this.logger = loggerFactory.CreateLogger<Catalog>();
            this.activationCollector = activationCollector;
            _catalogInstruments = catalogInstruments;
            _messagingProcessingInstruments = messagingProcessingInstruments;

            // 初始化锁定条纹阵列
            for (var i = 0; i < LockCount; i++)
            {
                _locks[i] = new();
            }

            GC.GetTotalMemory(true); // 需要拨打一次 w/ true / true 来保证虚假返回

            _messagingProcessingInstruments.RegisterActivationDataAllObserve(() =>
            {
                long counter = 0;
                foreach (var activation in activations)
                {
                    if (activation.Value is ActivationData data)
                    {
                        counter += data.GetRequestCount();
                    }
                }

                return counter;
            });
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        /// <summary>
        /// 使用一致的散列获取指定 grain ID的锁。
        /// </summary>
        /// <param name="grainId">用于获取锁定的 grain ID。</param>
        /// <returns>指定 grain ID 的锁定对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Lock GetStripedLock(in GrainId grainId)
        {
            var hash = grainId.GetUniformHashCode();
            var lockIndex = (int)(hash & LockMask);
            return _locks[lockIndex];
        }

        /// <summary>
        /// 取消对信件目标的登记, 停止发送给它的信件
        /// </summary>
        /// <param name="activation"></param>
        public void UnregisterMessageTarget(IGrainContext activation)
        {
            if (activations.RemoveTarget(activation))
            {
                LogTraceUnregisteredActivation(activation);

                // 一旦我们重新设置了解除代码路径, 便应该删除此选项。 现在安全保存 。
                activationCollector.TryCancelCollection(activation as ICollectibleGrainContext);
                _catalogInstruments.OnActivationDestroyed(GetGrainTypeMetricName(activation));
            }
        }

        /// <summary>
        /// 仅试验目的!
        /// </summary>
        /// <param name="grain"></param>
        internal int UnregisterGrainForTesting(GrainId grain)
        {
            var activation = activations.FindTarget(grain);
            if (activation is null) return 0;

            UnregisterMessageTarget(activation);
            return 1;
        }

        /// <summary>
        /// 如果已经启动, 退回它 。
        /// 否则,创造新的激活, 开始补水并启动它, 然后返回它。
        /// </summary>
        /// <remarks>
        /// 无法保证所归还的激活的有效性。
        /// 活动负责处理收到的任何信息。
        /// </remarks>
        /// <param name="grainId"> grain 身份。</param>
        /// <param name="requestContextData">请求提供任择背景数据。</param>
        /// <param name="migration">选择性移徙状态和申请。
        /// 在新激活开始补水和启动之前。 接收会删除软件包中的请求 。
        /// 如果返回无效, 调用者保留请求。 未处理未被接纳的请求。</param>
        public IGrainContext? GetOrCreateActivation(
            in GrainId grainId,
            Dictionary<string, object>? requestContextData,
            GrainMigrationPackage? migration = null)
        {
            Debug.Assert(migration is null || migration.Value.GrainId == grainId);
            var rehydrationContext = migration?.MigrationContext;
            try
            {
                if (TryGetGrainContext(grainId, out var result))
                {
                    ReceiveRequests(result, migration);
                    return result;
                }
                else if (grainId.IsSystemTarget())
                {
                    return null;
                }

                var created = false;
                lock (GetStripedLock(grainId))
                {
                    if (!TryGetGrainContext(grainId, out result) && _siloStatusOracle.CurrentStatus == SiloStatus.Active)
                    {
                        var address = new GrainAddress
                        {
                            SiloAddress = Silo,
                            GrainId = grainId,
                            ActivationId = ActivationId.NewId(),
                            MembershipVersion = MembershipVersion.MinValue,
                        };

                        result = this.grainActivator.CreateInstance(address);
                        activations.RecordNewTarget(result);
                        created = true;
                    }
                } // 结束锁定

                if (result is null)
                {
                    return UnableToCreateActivation(this, grainId);
                }

                if (!created)
                {
                    // 入境可以拒绝申请,同时完成一个呼叫员的接通,所以在目录锁外交付。
                    ReceiveRequests(result, migration);
                    return result;
                }

                try
                {
                    // 启动激活时, 如果有请求, 启动时间与父上下文的启动间隔
                    var parentContext = requestContextData.TryGetActivityContext();
                    var activationActivity = parentContext.HasValue
                        ? ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivateGrain, ActivityKind.Internal, parentContext.Value)
                        : ActivitySources.LifecycleGrainSource.StartActivity(ActivityNames.ActivateGrain, ActivityKind.Internal);
                    if (activationActivity is not null)
                    {
                        activationActivity.SetTag(ActivityTagKeys.GrainId, grainId.ToString());
                        activationActivity.SetTag(ActivityTagKeys.GrainType, grainId.Type.ToString());
                        activationActivity.SetTag(ActivityTagKeys.SiloId, Silo.ToString());
                        activationActivity.SetTag(ActivityTagKeys.ActivationCause, rehydrationContext is null ? "new" : "rehydrate");
                        if (result is ActivationData act)
                        {
                            activationActivity.SetTag(ActivityTagKeys.ActivationId, act.ActivationId.ToString());
                            act.SetActivationActivity(activationActivity);
                            activationActivity.AddEvent(new ActivityEvent("creating"));
                        }
                    }

                    _catalogInstruments.OnActivationCreated(GetGrainTypeMetricName(result));

                    // 在再水处理之前,在重新补充之前应接受申请,并可在目录中登记。
                    ReceiveRequests(result, migration);
                }
                finally
                {
                    // 促使我们承诺要安排启动时间 这些电话转换工作要靠背景来进行,因为环境本身就有故障
                    if (rehydrationContext is not null)
                    {
                        result.Rehydrate(rehydrationContext);
                        rehydrationContext = null;
                    }

                    result.Activate(requestContextData);
                }

                return result;
            }
            catch
            {
                if (migration?.Requests is { } requests)
                {
                    foreach (var request in requests)
                    {
                        request.Dispose();
                    }

                    requests.Clear();
                }

                throw;
            }
            finally
            {
                rehydrationContext?.Dispose();
            }

            static void ReceiveRequests(IGrainContext destination, GrainMigrationPackage? migration)
            {
                if (migration is not { Requests: { Count: > 0 } requests })
                {
                    return;
                }

                var admitted = 0;
                try
                {
                    foreach (var request in requests)
                    {
                        // ReceiveMessage 即使处理请求后失败,也拥有所有权。
                        admitted++;
                        destination.ReceiveMessage(request);
                    }
                }
                finally
                {
                    // 如果入场失败,只有剩下的后缀属于包件。
                    requests.RemoveRange(0, admitted);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static IGrainContext? UnableToCreateActivation(Catalog self, GrainId grainId)
            {
                // 未找到, 也没有开始放置新
                var status = self._siloStatusOracle.CurrentStatus;
                var isTerminating = status.IsTerminating();
                if (status is not SiloStatus.Active)
                {
                    self.LogDebugUnableToCreateActivationWhenNotActive(grainId);
                }
                else
                {
                    self.LogDebugUnableToCreateActivation(grainId);
                }

                if (self._catalogInstruments.NonExistentActivationsEnabled)
                {
                    var instruments = self._catalogInstruments;
                    var grainTypeName = instruments.TryGetGrainTypeMetrics(grainId.Type, out var metrics)
                        ? metrics.GrainTypeTagValue
                        : grainId.Type.ToString();
                    instruments.OnNonExistentActivation(grainTypeName);
                }

                var grainLocator = self.serviceProvider.GetRequiredService<GrainLocator>();
                grainLocator.InvalidateCache(grainId);
                if (!isTerminating)
                {
                    // 解除目标启动的登记 这样我们就不会收到虚假信息
                    // 时间的延迟(截至本文撰写时为一分钟)是为了处理不可能但可能进行的比赛,因为
                    // 请求之前,请求启动同一程序,并请求新的位置。
                    // 如果新安置的启动登记申请 不知怎么地在取消登记之前溜走了
                    // 我们要确保我们不会解除刚发明的激活
                    var address = new GrainAddress { SiloAddress = self.Silo, GrainId = grainId };
                    _ = self.UnregisterNonExistentActivation(address);
                }

                return null;
            }
        }

        private string GetGrainTypeMetricName(IGrainContext context) => context switch
        {
            ActivationData activation => activation.Shared.GrainTypeMetricName,
            StatelessWorkerGrainContext worker => worker.GrainTypeMetrics.GrainTypeTagValue,
            _ => _catalogInstruments.GetGrainTypeMetrics(context.GrainId.Type).GrainTypeTagValue
        };

        private async Task UnregisterNonExistentActivation(GrainAddress address)
        {
            try
            {
                var grainLocator = serviceProvider.GetRequiredService<GrainLocator>();
                await grainLocator.Unregister(address, UnregistrationCause.NonexistentActivation);
            }
            catch (Exception exc)
            {
                LogFailedToUnregisterNonExistingActivation(address, exc);
            }
        }

        /// <summary>
        /// 尝试获取激活运行时间数据
        /// </summary>
        private bool TryGetGrainContext(GrainId grainId, [NotNullWhen(true)] out IGrainContext? data)
        {
            data = activations.FindTarget(grainId);
            return data != null;
        }

        /// <summary>
        /// 优优美的解除引爆装置,等待它们完成
        /// 完成并进行未结交易,然后删除。
        /// 不得在启动范围内被召唤,因此可以等待。
        /// </summary>
        internal async Task DeactivateActivations(DeactivationReason reason, List<IGrainContext> list, CancellationToken cancellationToken)
        {
            if (list == null || list.Count == 0) return;

            LogDebugDeactivateActivations(list.Count);
            var options = new ParallelOptions
            {
                CancellationToken = CancellationToken.None,
                MaxDegreeOfParallelism = Environment.ProcessorCount * 512
            };
            await Parallel.ForEachAsync(list, options, (activation, _) =>
            {
                if (activation.GrainId.Type.IsSystemTarget())
                {
                    return ValueTask.CompletedTask;
                }

                activation.Deactivate(reason, cancellationToken);
                return new(activation.Deactivated);
            }).WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 解除目录中每个激活的功能,等待其失效完成。
        /// </summary>
        /// <remarks>
        /// 激活使目录处于最后同步的解除步骤,除非被弃置为卡住,
        /// 等待已经启动的拆除,包括他们排队申请的迁移。
        /// </remarks>
        public async Task DeactivateAllActivations(CancellationToken cancellationToken)
        {
            LogDebugDeactivateAllActivations();
            LogDebugDeactivateActivations(activations.Count);
            var reason = new DeactivationReason(DeactivationReasonCode.ShuttingDown, "This process is terminating.");
            var options = new ParallelOptions
            {
                CancellationToken = CancellationToken.None,
                MaxDegreeOfParallelism = Environment.ProcessorCount * 512
            };
            await Parallel.ForEachAsync(activations, options, (kv, _) =>
            {
                if (kv.Key.IsSystemTarget())
                {
                    return ValueTask.CompletedTask;
                }

                var activation = kv.Value;
                activation.Deactivate(reason, cancellationToken);
                return new(activation.Deactivated);
            }).WaitAsync(cancellationToken);
        }

        public Task DeleteActivations(
            List<GrainAddress> addresses,
            DeactivationReasonCode reasonCode,
            string reasonText,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deactivationReason = new DeactivationReason(reasonCode, reasonText);
            foreach (var activationAddress in addresses)
            {
                if (TryGetGrainContext(activationAddress.GrainId, out var grainContext)
                    && grainContext.Address.Equals(activationAddress))
                {
                    // 停止活动同步使失败者无法通行;拆卸和取消登记继续无休止地进行。
                    grainContext.Deactivate(deactivationReason, CancellationToken.None);
                }
            }

            return Task.CompletedTask;
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            // 什么都不做, 只要确保创建此实例, 这样它就可以在激活目录中注册 。
            _siloStatusOracle = serviceProvider.GetRequiredService<ISiloStatusOracle>();
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Unregistered activation {Activation}")]
        private partial void LogTraceUnregisteredActivation(IGrainContext activation);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "DeactivateActivations: {Count} activations.")]
        private partial void LogDebugDeactivateActivations(int count);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.Catalog_DeactivateAllActivations,
            Message = "DeactivateAllActivations."
        )]
        private partial void LogDebugDeactivateAllActivations();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unable to create activation for grain {GrainId} because this silo is not active.")]
        private partial void LogDebugUnableToCreateActivationWhenNotActive(GrainId grainId);

        [LoggerMessage(
            EventId = (int)ErrorCode.CatalogNonExistingActivation2,
            Level = LogLevel.Debug,
            Message = "Unable to create activation for grain {GrainId}"
        )]
        private partial void LogDebugUnableToCreateActivation(GrainId grainId);

        [LoggerMessage(
            EventId = (int)ErrorCode.Dispatcher_FailedToUnregisterNonExistingAct,
            Level = LogLevel.Warning,
            Message = "Failed to unregister non-existent activation {Address}"
        )]
        private partial void LogFailedToUnregisterNonExistingActivation(GrainAddress address, Exception exception);
    }
}

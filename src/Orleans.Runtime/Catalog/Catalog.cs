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

        // 创建激活实例时使用分段锁，以减少争用。
        private const int LockCount = 32; // 必须为 2 的幂。
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

            // 初始化分段锁数组。
            for (var i = 0; i < LockCount; i++)
            {
                _locks[i] = new();
            }

            GC.GetTotalMemory(true); // 需要先以 true 调用一次，以确保传入 false 时返回有效值。

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
        /// 使用一致性哈希获取特定 Grain ID 对应的锁。
        /// </summary>
        /// <param name="grainId">要获取其锁的 Grain ID。</param>
        /// <returns>指定 Grain ID 对应的锁对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Lock GetStripedLock(in GrainId grainId)
        {
            var hash = grainId.GetUniformHashCode();
            var lockIndex = (int)(hash & LockMask);
            return _locks[lockIndex];
        }

        /// <summary>
        /// 注销消息目标，停止向其投递消息。
        /// </summary>
        /// <param name="activation"></param>
        public void UnregisterMessageTarget(IGrainContext activation)
        {
            if (activations.RemoveTarget(activation))
            {
                LogTraceUnregisteredActivation(activation);

                // 重构停用代码路径后应移除此项，目前保留是安全的。
                activationCollector.TryCancelCollection(activation as ICollectibleGrainContext);
                _catalogInstruments.OnActivationDestroyed(GetGrainTypeMetricName(activation));
            }
        }

        /// <summary>
        /// 仅供测试使用！！
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
        /// 如果激活实例已存在，则返回它。
        /// 否则创建新的激活实例，开始恢复其状态并进行激活，然后返回它。
        /// </summary>
        /// <remarks>
        /// 不保证返回的激活实例处于有效状态。
        /// 激活实例负责处理收到的所有消息。
        /// </remarks>
        /// <param name="grainId">Grain 标识。</param>
        /// <param name="requestContextData">可选的请求上下文数据。</param>
        /// <param name="migration">可选的迁移状态及请求。返回的激活实例通过常规准入流程接收请求，
        /// 此过程发生在新激活实例开始恢复状态和激活之前。准入会将请求从数据包移除。
        /// 如果返回 null，则调用方保留请求；发生失败时，释放尚未接纳的请求。</param>
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
                } // 结束持锁区段。

                if (result is null)
                {
                    return UnableToCreateActivation(this, grainId);
                }

                if (!created)
                {
                    // 准入可能拒绝请求并同步完成调用方，因此在激活实例目录锁之外进行投递。
                    ReceiveRequests(result, migration);
                    return result;
                }

                try
                {
                    // 启动激活跟踪跨度，如果请求提供了父上下文则使用它。
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

                    // 在状态恢复可能释放上下文、激活实例可能注册到目录之前接纳请求。
                    ReceiveRequests(result, migration);
                }
                finally
                {
                    // 创建实例后必须安排激活。这些调用将工作移交给上下文，由其负责处理失败。
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
                        // 即使后续处理请求失败，ReceiveMessage 也已取得消息所有权。
                        admitted++;
                        destination.ReceiveMessage(request);
                    }
                }
                finally
                {
                    // 如果准入过程在中途失败，只有尚未接纳的后续请求仍归数据包所有。
                    requests.RemoveRange(0, admitted);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static IGrainContext? UnableToCreateActivation(Catalog self, GrainId grainId)
            {
                // 未找到实例，也未开始放置新实例。
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
                    // 注销目标激活实例，避免持续收到不应到达的消息。
                    // 时间延迟（编写此处时为一分钟）用于处理一种虽然不太可能但仍可能发生的竞态：
                    // 此请求抢在同一激活实例的另一条要求重新放置的请求之前到达。
                    // 如果新放置产生的激活实例注册请求意外抢在此注销操作之前完成，
                    // 必须确保不会注销刚创建的激活实例。
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
        /// 尝试获取激活实例的运行时数据。
        /// </summary>
        private bool TryGetGrainContext(GrainId grainId, [NotNullWhen(true)] out IGrainContext? data)
        {
            data = activations.FindTarget(grainId);
            return data != null;
        }

        /// <summary>
        /// 优雅停用激活实例，等待其完成
        /// 并提交尚未完成的事务，然后将其删除。
        /// 不应从激活上下文内部调用，以便能够等待此操作完成。
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
        /// 停用激活实例目录中的所有激活实例，并等待停用完成。
        /// </summary>
        /// <remarks>
        /// 除非因卡住而被放弃，否则激活实例在停用流程的最后一个同步步骤才离开激活实例目录，
        /// 因此这也会等待已经开始的停用流程，包括其排队请求的迁移。
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
                    // Deactivate 同步使确切的落选实例无法再被路由；拆除和注销继续异步进行。
                    grainContext.Deactivate(deactivationReason, CancellationToken.None);
                }
            }

            return Task.CompletedTask;
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            // 不执行任何操作，仅确保创建此实例，以使其能够自行注册到激活实例目录中。
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

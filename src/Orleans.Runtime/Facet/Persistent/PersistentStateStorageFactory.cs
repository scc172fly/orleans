using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Core;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Orleans.Storage;

namespace Orleans.Runtime
{
    /// <summary>
    /// 为 Grain 创建 <see cref="IPersistentState{TState}"/> 实例。
    /// </summary>
    /// <seealso cref="Orleans.Runtime.IPersistentStateFactory" />
    public class PersistentStateFactory : IPersistentStateFactory
    {
        /// <inheritdoc/>
        public IPersistentState<TState> Create<TState>(IGrainContext context, IPersistentStateConfiguration cfg)
        {
            var storageProvider = !string.IsNullOrWhiteSpace(cfg.StorageName)
                ? context.ActivationServices.GetKeyedService<IGrainStorage>(cfg.StorageName)
                : context.ActivationServices.GetService<IGrainStorage>();
            if (storageProvider == null)
            {
                ThrowMissingProviderException(context, cfg);
            }

            var fullStateName = GetFullStateName(context, cfg);
            return new PersistentState<TState>(fullStateName, context, storageProvider);
        }

        /// <summary>
        /// 获取持久化状态实例的存储记录名称。
        /// </summary>
        /// <param name="context">Grain 上下文。</param>
        /// <param name="cfg">持久化状态配置。</param>
        /// <returns>存储记录名称。</returns>
        protected virtual string GetFullStateName(IGrainContext context, IPersistentStateConfiguration cfg)
        {
            return cfg.StateName;
        }

        [DoesNotReturn]
        private static void ThrowMissingProviderException(IGrainContext context, IPersistentStateConfiguration cfg)
        {
            string errMsg;
            if (string.IsNullOrEmpty(cfg.StorageName))
            {
                errMsg = $"No default storage provider found loading grain type {context.GrainId.Type}.";
            }
            else
            {
                errMsg = $"No storage provider named \"{cfg.StorageName}\" found loading grain type {context.GrainId.Type}.";
            }

            throw new BadProviderConfigException(errMsg);
        }
    }

    internal sealed class PersistentState<TState> : StateStorageBridge<TState>, IPersistentState<TState>, ILifecycleObserver
    {
        public PersistentState(string stateName, IGrainContext context, IGrainStorage storageProvider) : base(stateName, context, storageProvider)
        {
            var lifecycle = context.ObservableLifecycle;
            lifecycle.Subscribe(RuntimeTypeNameFormatter.Format(GetType()), GrainLifecycleStage.SetupState, this);
            lifecycle.AddMigrationParticipant(this);
        }

        public Task OnStart(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            // 如果已通过状态恢复加载状态，则无需再次加载。
            if (IsStateInitialized)
            {
                return Task.CompletedTask;
            }

            return ReadStateAsync();
        }

        public Task OnStop(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

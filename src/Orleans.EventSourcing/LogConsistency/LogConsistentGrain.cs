using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Providers;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 用于管理状态时使用日志一致性的所有 grains 基级。
    /// 等于 <see cref="Grain{T}"/> 用于 grains 使用对数一致性。
    /// (SiloAssemblyLoader 使用它提取类型)
    /// </summary>
    /// <typeparam name="TView">视图类型</typeparam>
    public abstract class LogConsistentGrain<TView> : Grain, ILifecycleParticipant<IGrainLifecycle>
    {
        /// <summary>
        /// 在 grain 建造后立即调用安装日志视图适配器
        /// </summary>
        /// <param name="factory"> 调适工厂使用 </param>
        /// <param name="state"> 视图的初始状态 </param>
        /// <param name="grainTypeName"> grain </param>的名称类型
        /// <param name="grainStorage"> grain 储存,必要时储存 </param>
        /// <param name="services"> 礼宾服务 </param>
        protected abstract void InstallAdaptor(ILogViewAdaptorFactory factory, object state, string grainTypeName, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services);

        /// <summary>
        /// 获取默认调整器厂用,如果没有默认使用则为无效
        /// (在这种情况下用户必须配置一个一致性提供商)
        /// </summary>
        protected abstract ILogViewAdaptorFactory? DefaultAdaptorFactory { get; }

        /// <inheritdoc />
        public virtual void Participate(IGrainLifecycle lifecycle)
        {
            lifecycle.Subscribe<LogConsistentGrain<TView>>(GrainLifecycleStage.SetupState, OnSetupState, OnDeactivateState);
            if (this is ILogConsistencyProtocolParticipant)
            {
                lifecycle.Subscribe<LogConsistentGrain<TView>>(GrainLifecycleStage.Activate - 1, PreActivate);
                lifecycle.Subscribe<LogConsistentGrain<TView>>(GrainLifecycleStage.Activate + 1, PostActivate);
            }
        }

        private async Task OnDeactivateState(CancellationToken ct)
        {
            if (this is ILogConsistencyProtocolParticipant participant)
            {
                await participant.DeactivateProtocolParticipant(ct);
            }
        }

        private Task OnSetupState(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return Task.CompletedTask;
            IGrainContextAccessor grainContextAccessor = this.ServiceProvider.GetRequiredService<IGrainContextAccessor>();
            Factory<IGrainContext, ILogConsistencyProtocolServices> protocolServicesFactory = this.ServiceProvider.GetRequiredService<Factory<IGrainContext, ILogConsistencyProtocolServices>>();
            IGrainContext grainContext = grainContextAccessor.GrainContext!;
            ILogViewAdaptorFactory consistencyProvider = SetupLogConsistencyProvider(grainContext);
            IGrainStorage? grainStorage = consistencyProvider.UsesStorageProvider ? GrainStorageHelpers.GetGrainStorage((grainContext?.GrainInstance!.GetType())!, this.ServiceProvider) : null;
            InstallLogViewAdaptor(grainContext!, protocolServicesFactory, consistencyProvider, grainStorage);
            return Task.CompletedTask;
        }

        private async Task PreActivate(CancellationToken ct)
        {
            await ((ILogConsistencyProtocolParticipant)this).PreActivateProtocolParticipant(ct);
        }

        private async Task PostActivate(CancellationToken ct)
        {
            await ((ILogConsistencyProtocolParticipant)this).PostActivateProtocolParticipant(ct);
        }

        private void InstallLogViewAdaptor(
            IGrainContext grainContext,
            Factory<IGrainContext, ILogConsistencyProtocolServices> protocolServicesFactory,
            ILogViewAdaptorFactory factory,
            IGrainStorage? grainStorage)
        {
            // 一致性适配器使用的装封运行时间服务
            ILogConsistencyProtocolServices svc = protocolServicesFactory(grainContext);

            TView state = (TView)Activator.CreateInstance(typeof(TView))!;

            this.InstallAdaptor(factory, state, this.GetType().FullName!, grainStorage, svc);
        }

        private ILogViewAdaptorFactory SetupLogConsistencyProvider(IGrainContext activationContext)
        {
            var attr = this.GetType().GetCustomAttributes<LogConsistencyProviderAttribute>(true).FirstOrDefault();

            ILogViewAdaptorFactory? defaultFactory = attr != null
                ? this.ServiceProvider.GetKeyedService<ILogViewAdaptorFactory>(attr.ProviderName)
                : this.ServiceProvider.GetService<ILogViewAdaptorFactory>();
            if (attr != null && defaultFactory == null)
            {
                var errMsg = $"Cannot find consistency provider with Name={attr.ProviderName} for grain type {this.GetType().FullName}";
                throw new BadProviderConfigException(errMsg);
            }

            // 如果找不到默认值, 则使用默认值
            defaultFactory = defaultFactory ?? this.DefaultAdaptorFactory;
            if (defaultFactory == null)
            {
                var errMsg = $"No log consistency provider found loading grain type {this.GetType().FullName}";
                throw new BadProviderConfigException(errMsg);
            }
            ;

            return defaultFactory;
        }
    }
}

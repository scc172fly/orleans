using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Runtime.ConsistentRing;
using Orleans.Services;

namespace Orleans.Runtime.Services
{
    /// <summary>
    /// 根据适当的环分区策略将请求代理到相应的 GrainService。
    /// </summary>
    /// <typeparam name="TGrainService"></typeparam>
    public abstract class GrainServiceClient<TGrainService> : IGrainServiceClient<TGrainService> where TGrainService : IGrainService
    {
        private readonly IInternalGrainFactory grainFactory;
        private readonly IConsistentRingProvider ringProvider;
        private readonly GrainType grainType;

        /// <summary>
        /// 当前每个 Silo 仅支持一个 GrainService；支持多个后，将在此请求每个 Silo 要分区的 GrainService 数量。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        protected GrainServiceClient(IServiceProvider serviceProvider)
        {
            grainFactory = serviceProvider.GetRequiredService<IInternalGrainFactory>();
            ringProvider = serviceProvider.GetRequiredService<IConsistentRingProvider>();

            // GrainInterfaceMap 仅保存 IGrain 类型，而不保存 ISystemTarget 类型，因此通过 Orleans.CodeGeneration 解析。
            // 在合并之前解决此问题。
            var grainTypeCode = GrainInterfaceUtils.GetGrainClassTypeCode(typeof(TGrainService));
            grainType = SystemTargetGrainId.CreateGrainServiceGrainType(grainTypeCode, null);
        }

        /// <summary>
        /// 获取当前正在执行的 Grain 的引用。
        /// </summary>
        protected GrainReference CurrentGrainReference => RuntimeContext.Current?.GrainReference!;

        /// <summary>
        /// 获取负责处理请求的 <see cref="GrainService"/> 的引用，根据 <paramref name="callingGrainId"/> 确定该服务。
        /// </summary>
        protected TGrainService GetGrainService(GrainId callingGrainId)
        {
            return GetGrainService(callingGrainId.GetUniformHashCode());
        }

        /// <summary>
        /// 获取负责处理请求的 <see cref="GrainService"/> 的引用，根据 <paramref name="key"/> 确定该服务。
        /// </summary>
        protected TGrainService GetGrainService(uint key)
        {
            return GetGrainService(ringProvider.GetPrimaryTargetSilo(key)!);
        }

        /// <summary>
        /// 获取负责处理请求的 <see cref="GrainService"/> 的引用，根据 <paramref name="destination"/> 确定该服务。
        /// </summary>
        protected TGrainService GetGrainService(SiloAddress destination)
        {
            var grainId = SystemTargetGrainId.CreateGrainServiceGrainId(grainType, destination);
            var grainService = grainFactory.GetSystemTarget<TGrainService>(grainId);

            return grainService;
        }
    }
}

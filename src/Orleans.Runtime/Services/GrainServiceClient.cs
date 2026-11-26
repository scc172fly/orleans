using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Runtime.ConsistentRing;
using Orleans.Services;

namespace Orleans.Runtime.Services
{
    /// <summary>
    /// 代理人根据适当的环形分割战略向适当的 GrainService 提出要求。
    /// </summary>
    /// <typeparam name="TGrainService"></typeparam>
    public abstract class GrainServiceClient<TGrainService> : IGrainServiceClient<TGrainService> where TGrainService : IGrainService
    {
        private readonly IInternalGrainFactory grainFactory;
        private readonly IConsistentRingProvider ringProvider;
        private readonly GrainType grainType;

        /// <summary>
        /// 目前我们只支持一个 GrainService 百分比/ 百 Silo,当多个数得到支持时,我们将要求 GrainServices 分区对齐 silo 这里
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        protected GrainServiceClient(IServiceProvider serviceProvider)
        {
            grainFactory = serviceProvider.GetRequiredService<IInternalGrainFactory>();
            ringProvider = serviceProvider.GetRequiredService<IConsistentRingProvider>();

            // GrainInterfaceMap 仅持有IGRAIN类型,而不是 ISystemTarget 类型,通过 Orleans.CodeGeneration解决。
            // 合并前先解决此项 。
            var grainTypeCode = GrainInterfaceUtils.GetGrainClassTypeCode(typeof(TGrainService));
            grainType = SystemTargetGrainId.CreateGrainServiceGrainType(grainTypeCode, null);
        }

        /// <summary>
        /// 获取当前执行的引用 grain。
        /// </summary>
        protected GrainReference CurrentGrainReference => RuntimeContext.Current?.GrainReference!;

        /// <summary>
        /// 获取引用 <see cref="GrainService"/> 负责根据请求采取行动的 <paramref name="callingGrainId"/>.
        /// </summary>
        protected TGrainService GetGrainService(GrainId callingGrainId)
        {
            return GetGrainService(callingGrainId.GetUniformHashCode());
        }

        /// <summary>
        /// 获取引用 <see cref="GrainService"/> 负责根据请求采取行动的 <paramref name="key"/>.
        /// </summary>
        protected TGrainService GetGrainService(uint key)
        {
            return GetGrainService(ringProvider.GetPrimaryTargetSilo(key)!);
        }

        /// <summary>
        /// 获取引用 <see cref="GrainService"/> 负责根据请求采取行动的 <paramref name="destination"/>.
        /// </summary>
        protected TGrainService GetGrainService(SiloAddress destination)
        {
            var grainId = SystemTargetGrainId.CreateGrainServiceGrainId(grainType, destination);
            var grainService = grainFactory.GetSystemTarget<TGrainService>(grainId);

            return grainService;
        }
    }
}

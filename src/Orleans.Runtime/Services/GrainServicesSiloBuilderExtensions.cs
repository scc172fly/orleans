using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans.Hosting
{
    /// <summary>
    /// 登记 grain 服务推广方法。
    /// </summary>
    public static class GrainServicesSiloBuilderExtensions
    {
        /// <summary>
        /// 登记申请 grain 服务,该服务将从 silo开始。
        /// </summary>
        /// <typeparam name="T">下 grain 服务执行类型。</typeparam>
        /// <param name="builder">建筑工。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddGrainService<T>(this ISiloBuilder builder)
            where T : GrainService
        {
            return builder.ConfigureServices(services => services.AddGrainService<T>());
        }

        private static IGrainService GrainServiceFactory(Type serviceType, IServiceProvider services)
        {
            var grainServiceInterfaceType = Array.Find(serviceType.GetInterfaces(), x => x.GetInterfaces().Contains(typeof(IGrainService)));
            if (grainServiceInterfaceType is null)
            {
                throw new InvalidOperationException(string.Format($"Cannot find an interface on {serviceType.FullName} which implements IGrainService"));
            }

            var typeCode = GrainInterfaceUtils.GetGrainClassTypeCode(grainServiceInterfaceType);
            var grainId = SystemTargetGrainId.CreateGrainServiceGrainId(typeCode, null!, SiloAddress.Zero);
            var grainService = (IGrainService)ActivatorUtilities.CreateInstance(services, serviceType, grainId);
            return grainService;
        }

        /// <summary>
        /// 登记申请 grain 服务,该服务将从 silo开始。
        /// </summary>
        /// <typeparam name="T">下 grain 服务执行类型。</typeparam>
        /// <param name="services">服务收藏。</param>
        /// <returns>服务收藏。</returns>
        public static IServiceCollection AddGrainService<T>(this IServiceCollection services)
        {
            return services.AddGrainService(typeof(T));
        }

        /// <summary>
        /// 登记申请 grain 服务,该服务将从 silo开始。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="grainServiceType">下 grain 服务执行类型。</param>
        /// <returns>服务收藏。</returns>
        public static IServiceCollection AddGrainService(this IServiceCollection services, Type grainServiceType)
        {
            return services.AddSingleton<IGrainService>(sp => GrainServiceFactory(grainServiceType, sp));
        }
    }
}

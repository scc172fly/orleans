using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于注册 Grain 服务的扩展方法。
    /// </summary>
    public static class GrainServicesSiloBuilderExtensions
    {
        /// <summary>
        /// 注册随 Silo 一起启动的应用程序 Grain 服务。
        /// </summary>
        /// <typeparam name="T">Grain 服务的实现类型。</typeparam>
        /// <param name="builder">构建器。</param>
        /// <returns>Silo 构建器。</returns>
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
        /// 注册随 Silo 一起启动的应用程序 Grain 服务。
        /// </summary>
        /// <typeparam name="T">Grain 服务的实现类型。</typeparam>
        /// <param name="services">服务集合。</param>
        /// <returns>服务集合。</returns>
        public static IServiceCollection AddGrainService<T>(this IServiceCollection services)
        {
            return services.AddGrainService(typeof(T));
        }

        /// <summary>
        /// 注册随 Silo 一起启动的应用程序 Grain 服务。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="grainServiceType">Grain 服务的实现类型。</param>
        /// <returns>服务集合。</returns>
        public static IServiceCollection AddGrainService(this IServiceCollection services, Type grainServiceType)
        {
            return services.AddSingleton<IGrainService>(sp => GrainServiceFactory(grainServiceType, sp));
        }
    }
}

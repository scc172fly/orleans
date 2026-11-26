using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Configuration.Internal
{
    /// <summary>
    /// 配置依赖注射的扩展方法。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 登记 <typeparamref name="TImplementation"/> 作为服务类型 <typeparamref name="TService"/>的现有登记。
        /// </summary>
        /// <typeparam name="TService">所提供的服务类型。</typeparam>
        /// <typeparam name="TImplementation">执行《执行和执行执行执行 <typeparamref name="TService"/>.</typeparam>
        /// <param name="services">服务收藏。</param>
        public static void AddFromExisting<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService
        {
            services.AddFromExisting(typeof(TService), typeof(TImplementation));
        }

        /// <summary>
        /// 登记 <paramref name="implementation"/> 作为服务类型 <paramref name="service"/>的现有登记。
        /// </summary>
        /// <param name="services">服务收藏。</param>
        /// <param name="service">所提供的服务类型。</param>
        /// <param name="implementation">执行《执行和执行执行执行 <paramref name="service"/>.</param>
        public static void AddFromExisting(this IServiceCollection services, Type service, Type implementation)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(implementation);

            ServiceDescriptor? registration = null;
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType == implementation)
                {
                    registration = descriptor;
                    break;
                }
            }

            if (registration is null)
            {
                throw new ArgumentNullException(nameof(implementation), $"Unable to find previously registered ServiceType of '{implementation.FullName}'");
            }

            var newRegistration = new ServiceDescriptor(
                service,
                sp => sp.GetRequiredService(implementation),
                registration.Lifetime);
            services.Add(newRegistration);
        }

        /// <summary>
        /// 登记 <typeparamref name="TImplementation"/> 提供 <typeparamref name="TService"/> 如果没有 <typeparamref name="TService"/> 执行。
        /// </summary>
        /// <typeparam name="TService">所提供的服务类型。</typeparam>
        /// <typeparam name="TImplementation">执行《执行和执行执行执行 <typeparamref name="TService"/>.</typeparam>
        /// <param name="services">服务收藏。</param>
        public static void TryAddFromExisting<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService
        {
            if (services.All(service => service.ServiceType != typeof(TService)))
            {
                services.AddFromExisting<TService, TImplementation>();
            }
        }
    }
}

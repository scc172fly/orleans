using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Configuration.Internal
{
    /// <summary>
    /// 用于配置依赖注入的扩展方法。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 将 <typeparamref name="TImplementation"/> 的现有注册作为服务类型 <typeparamref name="TService"/> 的提供程序进行注册。
        /// </summary>
        /// <typeparam name="TService">所提供的服务类型。</typeparam>
        /// <typeparam name="TImplementation"><typeparamref name="TService"/> 的实现。</typeparam>
        /// <param name="services">服务集合。</param>
        public static void AddFromExisting<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService
        {
            services.AddFromExisting(typeof(TService), typeof(TImplementation));
        }

        /// <summary>
        /// 将 <paramref name="implementation"/> 的现有注册作为服务类型 <paramref name="service"/> 的提供程序进行注册。
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="service">所提供的服务类型。</param>
        /// <param name="implementation"><paramref name="service"/> 的实现。</param>
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
        /// 将 <typeparamref name="TImplementation"/> 的现有注册作为 <typeparamref name="TService"/> 的提供程序进行注册，前提是尚无 <typeparamref name="TService"/> 的实现。
        /// </summary>
        /// <typeparam name="TService">所提供的服务类型。</typeparam>
        /// <typeparam name="TImplementation"><typeparamref name="TService"/> 的实现。</typeparam>
        /// <param name="services">服务集合。</param>
        public static void TryAddFromExisting<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService
        {
            if (services.All(service => service.ServiceType != typeof(TService)))
            {
                services.AddFromExisting<TService, TImplementation>();
            }
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Tester.StorageFacet.Abstractions;

namespace Tester.StorageFacet.Infrastructure
{
    public static class ExampleStorageExtensions
    {
        public static void UseExampleStorage(this ISiloBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // 储存地物的工厂基础设施
                services.AddTransient<INamedExampleStorageFactory, NamedExampleStorageFactory>();

                // 存储特性 type att 属性映像仪
                services.AddSingleton(typeof(IAttributeToFactoryMapper<ExampleStorageAttribute>), typeof(ExampleStorageAttributeMapper));
            });
        }

        public static void UseAsDefaultExampleStorage<TFactoryType>(this ISiloBuilder builder)
            where TFactoryType : class, IExampleStorageFactory
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<IExampleStorageFactory, TFactoryType>();
            });
        }
    }
}

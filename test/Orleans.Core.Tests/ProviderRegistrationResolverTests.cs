#nullable enable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Orleans.Serialization.Configuration;
using Xunit;

namespace NonSilo.Tests
{
    /// <summary>
    /// 由下列机构进行的供应商发现测试 <see cref="ProviderRegistrationResolver"/>,使用共享接缝
    /// 客户端 (<c>DefaultClientServices</c>) 和 silo (<c>DefaultSiloServices</c>配置。
    /// 这些核查数据核实产生供应商元数据的元数据被消耗,由旧代码发电机制作的组件被消耗
    /// (缺乏) <see cref="IProviderMetadataProvider"/>) 通过 <see cref="RegisterProviderAttribute"/>
    /// 且在识别数据外发现该自定义类型的产品时,从未激活过。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("Providers")]
    public class ProviderRegistrationResolverTests
    {
        /// <summary>
        /// 证明生成的供应商元数据(一种类型,标记为: OrleansCodeGen 执行
        /// <see cref="IProviderMetadataProvider"/>)为客户和 silo 目标的发现所消耗,
        /// 每个目标只收到所申报的登记。
        /// </summary>
        [Fact]
        public void GeneratedProviderMetadata_IsConsumedByClientAndSiloConfiguration()
        {
            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(GeneratedMetadataProvider)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 客户发现仅接收生成的元数据中针对客户的登记。
            Assert.Equal(typeof(ClientClusteringProviderBuilder), clientProviders[("Clustering", "UnitTestClustering")]);
            Assert.False(clientProviders.ContainsKey(("GrainStorage", "UnitTestStorage")));

            // Silo 发现仅接收同一元数据的 silo- 目标登记。
            Assert.Equal(typeof(SiloStorageProviderBuilder), siloProviders[("GrainStorage", "UnitTestStorage")]);
            Assert.False(siloProviders.ContainsKey(("Clustering", "UnitTestClustering")));
        }

        /// <summary>
        /// 证明由旧代码发电机制作的组装装置----其生成型号显示器供应商
        /// 不执行 <see cref="IProviderMetadataProvider"/> - 仍然提供其供应商登记
        /// 穿越遗留 <see cref="RegisterProviderAttribute"/> 客户端和 silo 目标。
        /// </summary>
        [Fact]
        public void LegacyAssemblyWithoutProviderMetadata_UsesRegisterProviderAttributeFallback()
        {
            OldGeneratorMetadataProvider.InstantiationCount = 0;

            var assembly = CreateAssemblyWithAttributes(
                // 旧的发电机仍发出生成型号显示器供应商,但日期早于
                // 因此它没有执行IProviderMetadataProvider 。
                TypeManifestProviderAttribute(typeof(OldGeneratorMetadataProvider)),
                RegisterProviderAttribute("LegacyClustering", "Clustering", "Client", typeof(LegacyClientProviderBuilder)),
                RegisterProviderAttribute("LegacyStorage", "GrainStorage", "Silo", typeof(LegacySiloProviderBuilder)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 客户目标遗留登记是通过 RegisterProviderAttribute 后退发现的。
            Assert.Equal(typeof(LegacyClientProviderBuilder), clientProviders[("Clustering", "LegacyClustering")]);
            Assert.False(clientProviders.ContainsKey(("GrainStorage", "LegacyStorage")));

            // silo-- -- 目标遗留登记是通过同样的后退路径发现的。
            Assert.Equal(typeof(LegacySiloProviderBuilder), siloProviders[("GrainStorage", "LegacyStorage")]);
            Assert.False(siloProviders.ContainsKey(("Clustering", "LegacyClustering")));
            Assert.Equal(0, OldGeneratorMetadataProvider.InstantiationCount);
        }

        /// <summary>
        /// 证明自定义类型的货物清单提供者----未以 OrleansCodeGen 生成的标记为标志 - 是
        /// 用户发现供应商时从未进行即时同步。 只有 OrleansCodeGen- 生成的提供者在
        /// 注射自给性注射;通过DI通过序列配置管道启动定制供应商。
        /// </summary>
        [Fact]
        public void CustomTypeManifestProvider_IsNotActivatedByProviderDiscovery()
        {
            CustomManifestProvider.InstantiationCount = 0;

            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(CustomManifestProvider)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 自定义提供者不是 OrleansCodeGen- 生成的, 所以发现不能激活它 。
            Assert.Equal(0, CustomManifestProvider.InstantiationCount);

            // 因为它从未启动过, 它没有帮助任何目标的注册。
            Assert.Empty(clientProviders);
            Assert.Empty(siloProviders);
        }

        /// <summary>
        /// 证明装配组装配时同时携带现代产生的供应商元数据(实施)
        /// <see cref="IProviderMetadataProvider"/>和遗留 <see cref="RegisterProviderAttribute"/>,以及所生成的
        /// 元数据优先, 遗留的后退被抑制。 这可以保护退出的行为
        /// 仅对老发电机建造的组件适用。
        /// </summary>
        [Fact]
        public void GeneratedProviderMetadata_SuppressesRegisterProviderAttributeFallback()
        {
            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(GeneratedMetadataProvider)),
                // 一条由于生成元数据而必须忽略的条块 RegisterProviderAttribute 。
                RegisterProviderAttribute("StaleClustering", "Clustering", "Client", typeof(LegacyClientProviderBuilder)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");

            // 只有生成的元数据登记存在。
            Assert.Equal(typeof(ClientClusteringProviderBuilder), clientProviders[("Clustering", "UnitTestClustering")]);

            // 遗留 RegisterProviderAttribute 登记被取缔,从不作出贡献。
            Assert.False(clientProviders.ContainsKey(("Clustering", "StaleClustering")));
            Assert.Single(clientProviders);
        }

        [Fact]
        public void GeneratedRegistryProvider_DoesNotInitializeLegacyFallback()
        {
            ProviderMetadataRegistry.Register(new RegistryMetadataProvider());
            var fallbackCount = 0;
            var resolver = new ProviderRegistrationResolver(
                typeof(ProviderRegistrationResolver).Assembly,
                () =>
                {
                    Interlocked.Increment(ref fallbackCount);
                    return [];
                });

            var providers = resolver.GetRegisteredProviders("Client");

            Assert.True(resolver.TryGetRegisteredProvider(
                providers,
                "Client",
                "Clustering",
                "RegistryClustering",
                out var providerType));
            Assert.Equal(typeof(RegistryClientProviderBuilder), providerType);
            Assert.Equal(0, fallbackCount);
        }

        [Fact]
        public void MissingGeneratedProvider_InitializesLegacyFallbackOnceForAllTargets()
        {
            var fallbackCount = 0;
            var assembly = CreateAssemblyWithAttributes(
                RegisterProviderAttribute("LazyClient", "Clustering", "Client", typeof(LegacyClientProviderBuilder)),
                RegisterProviderAttribute("LazySilo", "GrainStorage", "Silo", typeof(LegacySiloProviderBuilder)));
            var resolver = new ProviderRegistrationResolver(
                typeof(ProviderRegistrationResolver).Assembly,
                () =>
                {
                    Interlocked.Increment(ref fallbackCount);
                    return [assembly];
                });

            var clientProviders = resolver.GetRegisteredProviders("Client");
            Assert.True(resolver.TryGetRegisteredProvider(
                clientProviders,
                "Client",
                "Clustering",
                "LazyClient",
                out var clientProviderType));
            Assert.Equal(typeof(LegacyClientProviderBuilder), clientProviderType);

            var siloProviders = resolver.GetRegisteredProviders("Silo");
            Assert.True(resolver.TryGetRegisteredProvider(
                siloProviders,
                "Silo",
                "GrainStorage",
                "LazySilo",
                out var siloProviderType));
            Assert.Equal(typeof(LegacySiloProviderBuilder), siloProviderType);

            Assert.False(resolver.TryGetRegisteredProvider(
                clientProviders,
                "Client",
                "Clustering",
                "Missing",
                out _));
            Assert.Equal(1, fallbackCount);
            Assert.Equal(typeof(LegacyClientProviderBuilder), clientProviders[("Clustering", "LazyClient")]);
        }

        [Fact]
        public void GeneratedRegistry_DuplicateRegistrationIsDeterministicUnderConcurrency()
        {
            Parallel.Invoke(
                static () => ProviderMetadataRegistry.Register(new ZDuplicateMetadataProvider()),
                static () => ProviderMetadataRegistry.Register(new ADuplicateMetadataProvider()),
                static () => ProviderMetadataRegistry.Register(new ZDuplicateMetadataProvider()));

            var resolver = new ProviderRegistrationResolver(
                typeof(ProviderRegistrationResolver).Assembly,
                static () => []);
            var providers = resolver.GetRegisteredProviders("Client");

            Assert.Equal(typeof(ZDuplicateProviderBuilder), providers[("Clustering", "DeterministicDuplicate")]);
        }

        [Fact]
        public void GeneratedRegistry_DoesNotRootCollectibleAssemblyLoadContext()
        {
            var loadContextReference = RegisterProviderInCollectibleContext();

            for (var attempt = 0; attempt < 10 && loadContextReference.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Assert.False(loadContextReference.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RegisterProviderInCollectibleContext()
        {
            var loadContext = new CollectibleProviderLoadContext();
            var assembly = loadContext.LoadFromAssemblyPath(typeof(ProviderRegistrationResolverTests).Assembly.Location);
            var providerType = assembly.GetType(typeof(CollectibleMetadataProvider).FullName!, throwOnError: true)!;
            var provider = (IProviderMetadataProvider)Activator.CreateInstance(providerType)!;
            ProviderMetadataRegistry.Register(provider);

            var result = new WeakReference(loadContext);
            loadContext.Unload();
            return result;
        }

        private static CustomAttributeBuilder TypeManifestProviderAttribute(Type providerType)
        {
            var ctor = typeof(TypeManifestProviderAttribute).GetConstructor(new[] { typeof(Type) })!;
            return new CustomAttributeBuilder(ctor, new object[] { providerType });
        }

        private static CustomAttributeBuilder RegisterProviderAttribute(string name, string kind, string target, Type type)
        {
            var ctor = typeof(RegisterProviderAttribute).GetConstructor(
                new[] { typeof(string), typeof(string), typeof(string), typeof(Type) })!;
            return new CustomAttributeBuilder(ctor, new object[] { name, kind, target, type });
        }

        private static Assembly CreateAssemblyWithAttributes(params CustomAttributeBuilder[] attributes)
        {
            var name = new AssemblyName("ProviderDiscoveryTest_" + Guid.NewGuid().ToString("N"));
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            foreach (var attribute in attributes)
            {
                assemblyBuilder.SetCustomAttribute(attribute);
            }

            assemblyBuilder.DefineDynamicModule("Main");
            return assemblyBuilder;
        }

        // 模拟生成的元数据提供者:标记为 OrleansCodeGen 所产生并付诸实施
        // IProviderMetadataProvider,所以发现它 激活并读取它的登记。
        [GeneratedCode("OrleansCodeGen", "1.0.0")]
        public sealed class GeneratedMetadataProvider : TypeManifestProviderBase, IProviderMetadataProvider
        {
            public void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers)
            {
                providers[("Client", "Clustering", "UnitTestClustering")] = typeof(ClientClusteringProviderBuilder);
                providers[("Silo", "GrainStorage", "UnitTestStorage")] = typeof(SiloStorageProviderBuilder);
            }

            protected override void ConfigureInner(TypeManifestOptions options)
            {
            }
        }

        // 模拟旧生成器生成元数据提供者的仿真:标记为 OrleansCodeGen 生成,但
        // 突变 IProviderMetadataProvider, 所以发现必须返回到 RegisterProviderAttribute。
        [GeneratedCode("OrleansCodeGen", "1.0.0")]
        public sealed class OldGeneratorMetadataProvider : TypeManifestProviderBase
        {
            public static int InstantiationCount;

            public OldGeneratorMetadataProvider()
            {
                Interlocked.Increment(ref InstantiationCount);
            }

            protected override void ConfigureInner(TypeManifestOptions options)
            {
            }
        }

        // 模拟用户自定义的自定义类型表提供商。它是一个有效的 ITypeManifestProvider (因此可以
        // 引用引用 TypeManifestProviderAttribute),但并非 OrleansCodeGen- 产生,所以供应商的发现必须
        // 即时计数器证明建筑工是否运行了
        public sealed class CustomManifestProvider : TypeManifestProviderBase
        {
            public static int InstantiationCount;

            public CustomManifestProvider()
            {
                Interlocked.Increment(ref InstantiationCount);
            }

            protected override void ConfigureInner(TypeManifestOptions options)
            {
            }
        }

        // 标记类型仅用作供应商登记值;发现类型存储,而不激活。
        public sealed class ClientClusteringProviderBuilder
        {
        }

        public sealed class SiloStorageProviderBuilder
        {
        }

        public sealed class LegacyClientProviderBuilder
        {
        }

        public sealed class LegacySiloProviderBuilder
        {
        }

        public sealed class RegistryMetadataProvider : IProviderMetadataProvider
        {
            public void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers)
                => providers[("Client", "Clustering", "RegistryClustering")] = typeof(RegistryClientProviderBuilder);
        }

        public sealed class ADuplicateMetadataProvider : IProviderMetadataProvider
        {
            public void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers)
                => providers[("Client", "Clustering", "DeterministicDuplicate")] = typeof(ADuplicateProviderBuilder);
        }

        public sealed class ZDuplicateMetadataProvider : IProviderMetadataProvider
        {
            public void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers)
                => providers[("Client", "Clustering", "DeterministicDuplicate")] = typeof(ZDuplicateProviderBuilder);
        }

        public sealed class RegistryClientProviderBuilder
        {
        }

        public sealed class ADuplicateProviderBuilder
        {
        }

        public sealed class ZDuplicateProviderBuilder
        {
        }

        private sealed class CollectibleProviderLoadContext : AssemblyLoadContext
        {
            public CollectibleProviderLoadContext()
                : base(isCollectible: true)
            {
            }

            protected override Assembly? Load(AssemblyName assemblyName)
                => string.Equals(
                    assemblyName.Name,
                    typeof(IProviderMetadataProvider).Assembly.GetName().Name,
                    StringComparison.Ordinal)
                    ? typeof(IProviderMetadataProvider).Assembly
                    : null;
        }
    }

    public sealed class CollectibleMetadataProvider : IProviderMetadataProvider
    {
        public void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers)
            => providers[("Client", "Clustering", "Collectible")] = typeof(CollectibleProviderBuilder);
    }

    public sealed class CollectibleProviderBuilder
    {
    }
}

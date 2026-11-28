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
    /// 测试由 <see cref="ProviderRegistrationResolver"/> 执行的提供程序发现；该组件是
    /// 客户端（<c>DefaultClientServices</c>）和 Silo（<c>DefaultSiloServices</c>）配置共享的接入点。
    /// 验证生成的提供程序元数据被使用，并验证旧版代码生成器所生成的程序集
    /// （缺少 <see cref="IProviderMetadataProvider"/>）仍能通过 <see cref="RegisterProviderAttribute"/>
    /// 回退机制正常工作，并且自定义类型清单提供程序绝不会在 DI 之外的发现阶段被激活。
    /// </summary>
    [TestCategory("BVT")]
    [TestCategory("Providers")]
    public class ProviderRegistrationResolverTests
    {
        /// <summary>
        /// 验证生成的提供程序元数据（由 OrleansCodeGen 标记为已生成、且实现
        /// <see cref="IProviderMetadataProvider"/> 的类型）会在客户端和 Silo 两种目标的发现过程中使用，
        /// 且每个目标仅接收为其声明的注册项。
        /// </summary>
        [Fact]
        public void GeneratedProviderMetadata_IsConsumedByClientAndSiloConfiguration()
        {
            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(GeneratedMetadataProvider)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 客户端发现仅接收生成的元数据中面向客户端的注册项。
            Assert.Equal(typeof(ClientClusteringProviderBuilder), clientProviders[("Clustering", "UnitTestClustering")]);
            Assert.False(clientProviders.ContainsKey(("GrainStorage", "UnitTestStorage")));

            // Silo 发现仅接收同一生成元数据中面向 Silo 的注册项。
            Assert.Equal(typeof(SiloStorageProviderBuilder), siloProviders[("GrainStorage", "UnitTestStorage")]);
            Assert.False(siloProviders.ContainsKey(("Clustering", "UnitTestClustering")));
        }

        /// <summary>
        /// 验证由旧版代码生成器生成的程序集，该程序集的生成类型清单提供程序
        /// 未实现 <see cref="IProviderMetadataProvider"/>，仍会提供其注册项，
        /// 通过旧版 <see cref="RegisterProviderAttribute"/> 回退机制，同时适用于客户端和 Silo 目标。
        /// </summary>
        [Fact]
        public void LegacyAssemblyWithoutProviderMetadata_UsesRegisterProviderAttributeFallback()
        {
            OldGeneratorMetadataProvider.InstantiationCount = 0;

            var assembly = CreateAssemblyWithAttributes(
                // 旧版生成器仍会生成类型清单提供程序，但其出现早于
                // IProviderMetadataProvider，因此未实现该接口。
                TypeManifestProviderAttribute(typeof(OldGeneratorMetadataProvider)),
                RegisterProviderAttribute("LegacyClustering", "Clustering", "Client", typeof(LegacyClientProviderBuilder)),
                RegisterProviderAttribute("LegacyStorage", "GrainStorage", "Silo", typeof(LegacySiloProviderBuilder)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 通过 RegisterProviderAttribute 回退机制发现面向客户端的旧版注册项。
            Assert.Equal(typeof(LegacyClientProviderBuilder), clientProviders[("Clustering", "LegacyClustering")]);
            Assert.False(clientProviders.ContainsKey(("GrainStorage", "LegacyStorage")));

            // 通过相同的回退路径发现面向 Silo 的旧版注册项。
            Assert.Equal(typeof(LegacySiloProviderBuilder), siloProviders[("GrainStorage", "LegacyStorage")]);
            Assert.False(siloProviders.ContainsKey(("Clustering", "LegacyClustering")));
            Assert.Equal(0, OldGeneratorMetadataProvider.InstantiationCount);
        }

        /// <summary>
        /// 验证自定义类型清单提供程序（未被 OrleansCodeGen 标记为生成的提供程序）
        /// 绝不会由提供程序发现过程实例化。只有 OrleansCodeGen 生成的提供程序才会在
        /// 依赖注入之外激活；自定义提供程序由序列化配置管道通过 DI 激活。
        /// </summary>
        [Fact]
        public void CustomTypeManifestProvider_IsNotActivatedByProviderDiscovery()
        {
            CustomManifestProvider.InstantiationCount = 0;

            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(CustomManifestProvider)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");
            var siloProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Silo");

            // 该自定义提供程序并非由 OrleansCodeGen 生成，因此发现过程不得激活它。
            Assert.Equal(0, CustomManifestProvider.InstantiationCount);

            // 且由于从未激活，它不会向任何一个目标提供注册项。
            Assert.Empty(clientProviders);
            Assert.Empty(siloProviders);
        }

        /// <summary>
        /// 验证程序集同时带有现代的生成式提供程序元数据（实现
        /// <see cref="IProviderMetadataProvider"/>）及旧版 <see cref="RegisterProviderAttribute"/> 时，生成的
        /// 元数据具有优先权，旧版回退机制会被抑制。这确保回退机制的行为
        /// 仅应用于较旧生成器构建的程序集。
        /// </summary>
        [Fact]
        public void GeneratedProviderMetadata_SuppressesRegisterProviderAttributeFallback()
        {
            var assembly = CreateAssemblyWithAttributes(
                TypeManifestProviderAttribute(typeof(GeneratedMetadataProvider)),
                // 存在生成元数据，因此必须忽略这个过时的 RegisterProviderAttribute。
                RegisterProviderAttribute("StaleClustering", "Clustering", "Client", typeof(LegacyClientProviderBuilder)));

            var clientProviders = ProviderRegistrationResolver.GetRegisteredProviders(new[] { assembly }, "Client");

            // 仅存在生成的元数据注册项。
            Assert.Equal(typeof(ClientClusteringProviderBuilder), clientProviders[("Clustering", "UnitTestClustering")]);

            // 旧版 RegisterProviderAttribute 注册项受到抑制，不会提供任何注册项。
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

        // 模拟生成式元数据提供程序：由 OrleansCodeGen 标记为已生成且实现
        // IProviderMetadataProvider，因此发现过程会激活它并读取其注册项。
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

        // 模拟旧生成器生成的元数据提供程序：由 OrleansCodeGen 标记为已生成，但其生成时间
        // 早于 IProviderMetadataProvider，因此发现过程必须回退到 RegisterProviderAttribute。
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

        // 模拟用户编写的自定义类型清单提供程序。它是有效的 ITypeManifestProvider（因此可由
        // TypeManifestProviderAttribute 引用），但并非由 OrleansCodeGen 生成，因此提供程序发现过程
        // 绝不能构造它。实例化计数器可证明构造函数是否执行过。
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

        // 仅用作提供程序注册值的标记类型；发现过程存储 Type 而不激活该类型。
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

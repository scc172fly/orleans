using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.Runtime.Versions;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;

namespace Orleans.GrainReferences
{
    /// <summary>
    /// 创建 <see cref="GrainReference"/> 实例的中心入口。
    /// </summary>
    public sealed class GrainReferenceActivator
    {
        private readonly Lock _lockObj = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly IGrainReferenceActivatorProvider[] _providers;
        private Dictionary<(GrainType, GrainInterfaceType), IGrainReferenceActivator> _activators = new();

        /// <summary>
        /// 初始化 <see cref="GrainReferenceActivator"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="providers">Grain 引用激活器提供程序的集合。</param>
        public GrainReferenceActivator(
            IServiceProvider serviceProvider,
            IEnumerable<IGrainReferenceActivatorProvider> providers)
        {
            _serviceProvider = serviceProvider;
            _providers = providers.ToArray();
        }

        /// <summary>
        /// 创建指向指定 Grain ID 并实现指定 Grain 接口类型的 Grain 引用。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="interfaceType">Grain 接口类型。</param>
        /// <returns>新的 Grain 引用。</returns>
        public GrainReference CreateReference(GrainId grainId, GrainInterfaceType interfaceType)
        {
            if (!_activators.TryGetValue((grainId.Type, interfaceType), out var entry))
            {
                entry = CreateActivator(grainId.Type, interfaceType);
            }

            var result = entry.CreateReference(grainId);
            return result;
        }

        /// <summary>
        /// 根据所提供的参数创建 Grain 引用激活器。
        /// </summary>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="interfaceType">Grain 接口类型。</param>
        /// <returns>适用于所提供参数的激活器。</returns>
        /// <exception cref="InvalidOperationException">未找到合适的激活器。</exception>
        private IGrainReferenceActivator CreateActivator(GrainType grainType, GrainInterfaceType interfaceType)
        {
            lock (_lockObj)
            {
                if (!_activators.TryGetValue((grainType, interfaceType), out var entry))
                {
                    IGrainReferenceActivator? activator = null;
                    foreach (var provider in _providers)
                    {
                        if (provider.TryGet(grainType, interfaceType, out activator))
                        {
                            break;
                        }
                    }

                    if (activator is null)
                    {
                        throw new InvalidOperationException($"Unable to find an {nameof(IGrainReferenceActivatorProvider)} for grain type {grainType}");
                    }

                    entry = activator;
                    _activators = new(_activators) { [(grainType, interfaceType)] = entry };
                }

                return entry;
            }
        }
    }

    /// <summary>
    /// 创建仅指定目标 Grain ID 而未指定 Grain 接口的 Grain 引用。
    /// </summary>
    internal class UntypedGrainReferenceActivatorProvider : IGrainReferenceActivatorProvider
    {
        private readonly CopyContextPool _copyContextPool;
        private readonly CodecProvider _codecProvider;
        private readonly GrainVersionManifest _versionManifest;
        private readonly IServiceProvider _serviceProvider;
        private IGrainReferenceRuntime? _grainReferenceRuntime;

        /// <summary>
        /// 初始化 <see cref="UntypedGrainReferenceActivatorProvider"/> 类的新实例。
        /// </summary>
        /// <param name="manifest">Grain 版本清单。</param>
        /// <param name="copyContextPool">复制上下文池。</param>
        /// <param name="codecProvider">序列化编解码器提供程序。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        public UntypedGrainReferenceActivatorProvider(
            GrainVersionManifest manifest,
            CodecProvider codecProvider,
            CopyContextPool copyContextPool,
            IServiceProvider serviceProvider)
        {
            _versionManifest = manifest;
            _codecProvider = codecProvider;
            _copyContextPool = copyContextPool;
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, [NotNullWhen(true)] out IGrainReferenceActivator? activator)
        {
            if (!interfaceType.IsDefault)
            {
                activator = default;
                return false;
            }

            var interfaceVersion = _versionManifest.GetLocalVersion(interfaceType);

            var runtime = _grainReferenceRuntime ??= _serviceProvider.GetRequiredService<IGrainReferenceRuntime>();
            var shared = new GrainReferenceShared(
                grainType,
                interfaceType,
                interfaceVersion,
                runtime,
                InvokeMethodOptions.None,
                _codecProvider,
                _copyContextPool,
                _serviceProvider);
            activator = new UntypedGrainReferenceActivator(shared);
            return true;
        }

        /// <summary>
        /// 用于仅具有目标 Grain ID 而未指定 Grain 接口的 Grain 引用的激活器。
        /// </summary>
        private class UntypedGrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly GrainReferenceShared _shared;

            /// <summary>
            /// 初始化 <see cref="UntypedGrainReferenceActivator "/> 类的新实例。
            /// </summary>
            /// <param name="shared">给定类型的所有 Grain 共享的功能。</param>
            public UntypedGrainReferenceActivator(GrainReferenceShared shared)
            {
                _shared = shared;
            }

            /// <inheritdoc />
            public GrainReference CreateReference(GrainId grainId)
            {
                return GrainReference.FromGrainId(_shared, grainId);
            }
        }
    }

    /// <summary>
    /// 提供从 <see cref="GrainInterfaceType"/> 映射到对应的生成代理类型的功能。
    /// </summary>
    internal class RpcProvider
    {
        private readonly TypeConverter _typeConverter;
        private readonly Dictionary<GrainInterfaceType, Type> _mapping;

        /// <summary>
        /// 初始化 <see cref="RpcProvider"/> 类的新实例。
        /// </summary>
        /// <param name="config">本地类型清单。</param>
        /// <param name="resolver">Grain 接口类型到 Grain 类型的解析器。</param>
        /// <param name="typeConverter">用于泛型参数的类型转换器。</param>
        public RpcProvider(
            IOptions<TypeManifestOptions> config,
            GrainInterfaceTypeResolver resolver,
            TypeConverter typeConverter)
        {
            _typeConverter = typeConverter;
            var proxyTypes = config.Value.InterfaceProxyTypes;
            _mapping = new Dictionary<GrainInterfaceType, Type>();
            foreach (var proxyType in proxyTypes)
            {
                if (!typeof(IAddressable).IsAssignableFrom(proxyType))
                {
                    continue;
                }

                var type = proxyType switch
                {
                    { IsGenericType: true } => proxyType.GetGenericTypeDefinition(),
                    _ => proxyType
                };

                var grainInterface = GetMainInterface(type);
                var id = resolver.GetGrainInterfaceType(grainInterface);
                _mapping[id] = type;
            }

            [UnconditionalSuppressMessage(
                "Trimming",
                "IL2070",
                Justification = "TypeManifestOptions.AddInterfaceProxy preserves registered proxy interfaces before the types enter the manifest collection.")]
            static Type GetMainInterface(Type t)
            {
                var all = t.GetInterfaces();
                Type? result = null;
                foreach (var candidate in all)
                {
                    if (result is null)
                    {
                        result = candidate;
                    }
                    else
                    {
                        if (result.IsAssignableFrom(candidate))
                        {
                            result = candidate;
                        }
                    }
                }

                return result switch
                {
                    { IsGenericType: true } => result!.GetGenericTypeDefinition(),
                    _ => result!
                };
            }
        }

        /// <summary>
        /// 获取与指定 <see cref="GrainInterfaceType"/> 对应的生成代理对象类型。
        /// </summary>
        /// <param name="interfaceType">Grain 接口类型。</param>
        /// <param name="result">代理对象类型。</param>
        /// <returns>指示是否找到合适的类型且该类型能够被构造的值。</returns>
        public bool TryGet(GrainInterfaceType interfaceType, [NotNullWhen(true)] out Type? result)
        {
            GrainInterfaceType lookupId;
            Type[]? args;
            if (GenericGrainInterfaceType.TryParse(interfaceType, out var genericId))
            {
                lookupId = genericId.GetGenericGrainType().Value;
                args = genericId.GetArguments(_typeConverter);
            }
            else
            {
                lookupId = interfaceType;
                args = default;
            }

            if (!_mapping.TryGetValue(lookupId, out result))
            {
                return false;
            }

            if (args is not null)
            {
                result = result.MakeGenericType(args);
            }

            return true;
        }
    }

    /// <summary>
    /// 使用生成的代理对象创建 Grain 引用。
    /// </summary>
    internal class GrainReferenceActivatorProvider : IGrainReferenceActivatorProvider
    {
        private readonly CopyContextPool _copyContextPool;
        private readonly CodecProvider _codecProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly GrainPropertiesResolver _propertiesResolver;
        private readonly RpcProvider _rpcProvider;
        private readonly GrainVersionManifest _grainVersionManifest;
        private IGrainReferenceRuntime? _grainReferenceRuntime;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceActivatorProvider"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="propertiesResolver">Grain 属性解析器。</param>
        /// <param name="rpcProvider">代理对象类型提供程序。</param>
        /// <param name="copyContextPool">复制上下文池。</param>
        /// <param name="codecProvider">序列化编解码器提供程序。</param>
        /// <param name="grainVersionManifest">Grain 版本清单。</param>
        public GrainReferenceActivatorProvider(
            IServiceProvider serviceProvider,
            GrainPropertiesResolver propertiesResolver,
            RpcProvider rpcProvider,
            CopyContextPool copyContextPool,
            CodecProvider codecProvider,
            GrainVersionManifest grainVersionManifest)
        {
            _serviceProvider = serviceProvider;
            _propertiesResolver = propertiesResolver;
            _rpcProvider = rpcProvider;
            _copyContextPool = copyContextPool;
            _codecProvider = codecProvider;
            _grainVersionManifest = grainVersionManifest;
        }

        /// <inheritdoc />
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2067",
            Justification = "Generated proxies are registered using TypeManifestOptions.AddInterfaceProxy, which preserves their public constructors before they enter RpcProvider's mapping.")]
        public bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, [NotNullWhen(true)] out IGrainReferenceActivator? activator)
        {
            if (!_rpcProvider.TryGet(interfaceType, out var proxyType))
            {
                activator = default;
                return false;
            }

            var unordered = false;
            var properties = _propertiesResolver.GetGrainProperties(grainType);
            if (properties.Properties.TryGetValue(WellKnownGrainTypeProperties.Unordered, out var unorderedString)
                && string.Equals("true", unorderedString, StringComparison.OrdinalIgnoreCase))
            {
                unordered = true;
            }

            var interfaceVersion = _grainVersionManifest.GetLocalVersion(interfaceType);

            var invokeMethodOptions = unordered ? InvokeMethodOptions.Unordered : InvokeMethodOptions.None;
            var runtime = _grainReferenceRuntime ??= _serviceProvider.GetRequiredService<IGrainReferenceRuntime>();
            var shared = new GrainReferenceShared(
                grainType,
                interfaceType,
                interfaceVersion,
                runtime,
                invokeMethodOptions,
                _codecProvider,
                _copyContextPool,
                _serviceProvider);
            activator = new GrainReferenceActivator(proxyType, shared);
            return true;
        }

        /// <summary>
        /// 为给定 Grain 类型和 Grain 接口类型创建 Grain 引用。
        /// </summary>
        private sealed class GrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly GrainReferenceShared _shared;
            private readonly Func<GrainReferenceShared, IdSpan, GrainReference> _create;

            /// <summary>
            /// 初始化 <see cref="GrainReferenceActivator"/> 类的新实例。
            /// </summary>
            /// <param name="referenceType">生成的代理对象类型。</param>
            /// <param name="shared">指定 Grain 类型及 Grain 接口类型的所有 Grain 引用共享的功能。</param>
            [UnconditionalSuppressMessage(
                "Trimming",
                "IL2070",
                Justification = "Generated proxy public constructors are preserved by AddInterfaceProxy. Non-public constructors remain supported for explicitly preserved custom proxies.")]
            public GrainReferenceActivator(
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type referenceType,
                GrainReferenceShared shared)
            {
                _shared = shared;

                var ctor = referenceType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new[] { typeof(GrainReferenceShared), typeof(IdSpan) })
                    ?? throw new SerializerException("Invalid proxy type: " + referenceType);

                if (!RuntimeFeature.IsDynamicCodeSupported)
                {
                    var invoker = ConstructorInvoker.Create(ctor);
                    _create = (shared, key) => (GrainReference)invoker.Invoke(shared, key);
                    return;
                }

                var method = new DynamicMethod(referenceType.Name, typeof(GrainReference), new[] { typeof(object), typeof(GrainReferenceShared), typeof(IdSpan) });
                var il = method.GetILGenerator();
                // 为提高委托性能，不使用 arg0（避免参数重新排列的转换桩）。
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                _create = method.CreateDelegate<Func<GrainReferenceShared, IdSpan, GrainReference>>();
            }

            public GrainReference CreateReference(GrainId grainId) => _create(_shared, grainId.Key);
        }
    }

    /// <summary>
    /// 用于获取合适的 <see cref="IGrainReferenceActivator"/> 的功能，依据给定的 <see cref="GrainType"/> 和 <see cref="GrainInterfaceType"/>。
    /// </summary>
    public interface IGrainReferenceActivatorProvider
    {
        /// <summary>
        /// 为所提供的参数获取 Grain 引用激活器。
        /// </summary>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="interfaceType">Grain 接口类型。</param>
        /// <param name="activator">Grain 激活器。</param>
        /// <returns>指示是否找到合适 Grain 激活器的值。</returns>
        bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, [NotNullWhen(true)] out IGrainReferenceActivator? activator);
    }

    /// <summary>
    /// 创建 Grain 引用。
    /// </summary>
    public interface IGrainReferenceActivator
    {
        /// <summary>
        /// 创建新的 Grain 引用。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <returns>新的 Grain 引用。</returns>
        public GrainReference CreateReference(GrainId grainId);
    }
}

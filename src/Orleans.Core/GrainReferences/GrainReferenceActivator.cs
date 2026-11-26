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
    /// 创建 <see cref="GrainReference"/> 实例的中心点。
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
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="providers">收藏集 grain 参考激活器提供者。</param>
        public GrainReferenceActivator(
            IServiceProvider serviceProvider,
            IEnumerable<IGrainReferenceActivatorProvider> providers)
        {
            _serviceProvider = serviceProvider;
            _providers = providers.ToArray();
        }

        /// <summary>
        /// 创建 grain 引用点,指向指定的 grain id,并落实指定的 grain 接口类型。
        /// </summary>
        /// <param name="grainId">下 grain id. 数据。</param>
        /// <param name="interfaceType"> grain 接口类型。</param>
        /// <returns>新的 grain 参考。</returns>
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
        /// 为所提供的参数创建 grain 参考激活器。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="interfaceType"> grain 接口类型。</param>
        /// <returns>提供论点的激活者。</returns>
        /// <exception cref="InvalidOperationException">找不到合适的活动器。</exception>
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
    /// 创建 grain 没有指定引用 grain 接口,仅目标 grain id. 数据。
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
        /// <param name="manifest">下 grain 显示文本。</param>
        /// <param name="copyContextPool">复制背景库。</param>
        /// <param name="codecProvider">序列编码提供商</param>
        /// <param name="serviceProvider">服务供应商。</param>
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
        /// 激活器 grain 没有指定引用 grain 接口,仅目标 grain id. 数据。
        /// </summary>
        private class UntypedGrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly GrainReferenceShared _shared;

            /// <summary>
            /// 初始化 <see cref="UntypedGrainReferenceActivator "/> 类的新实例。
            /// </summary>
            /// <param name="shared">人人共享的功能 grains 某一类型。</param>
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
    /// 提供从 <see cref="GrainInterfaceType"/> 映射到相应的代用类型(Q0Q)的功能。
    /// </summary>
    internal class RpcProvider
    {
        private readonly TypeConverter _typeConverter;
        private readonly Dictionary<GrainInterfaceType, Type> _mapping;

        /// <summary>
        /// 初始化 <see cref="RpcProvider"/> 类的新实例。
        /// </summary>
        /// <param name="config">本地类型清单。</param>
        /// <param name="resolver">下 grain 界面类型 grain 类型解析器。</param>
        /// <param name="typeConverter">通用参数的型号转换器。</param>
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
        /// 获取与指定 <see cref="GrainInterfaceType"/>相对应的生成代理对象类型。
        /// </summary>
        /// <param name="interfaceType"> grain 接口类型。</param>
        /// <param name="result">代理对象类型。</param>
        /// <returns>表明是否找到合适类型并能够构建的数值。</returns>
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
    /// 使用生成的代理对象创建 grain 引用。
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
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="propertiesResolver">下 grain 财产结算程序。</param>
        /// <param name="rpcProvider">代理对象类型提供者。</param>
        /// <param name="copyContextPool">复制背景库。</param>
        /// <param name="codecProvider">序列编码提供商</param>
        /// <param name="grainVersionManifest">下 grain 显示文本。</param>
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
        /// 创建 grain 指定引用的参考 grain 和类型 grain 界面类型。
        /// </summary>
        private sealed class GrainReferenceActivator : IGrainReferenceActivator
        {
            private readonly GrainReferenceShared _shared;
            private readonly Func<GrainReferenceShared, IdSpan, GrainReference> _create;

            /// <summary>
            /// 初始化 <see cref="GrainReferenceActivator"/> 类的新实例。
            /// </summary>
            /// <param name="referenceType">生成的代理对象类型。</param>
            /// <param name="shared">人人共享的功能 grain 指定引用的指定引用 grain 和类型 grain 界面类型。</param>
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
                // arg0 用于更好地履行代表职能(避免辩论打折)
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
    /// 适当利用 <see cref="IGrainReferenceActivator"/> 给定 <see cref="GrainType"/> 和 <see cref="GrainInterfaceType"/>.
    /// </summary>
    public interface IGrainReferenceActivatorProvider
    {
        /// <summary>
        /// 获得提供参数的 grain 参考激活器。
        /// </summary>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="interfaceType"> grain 接口类型。</param>
        /// <param name="activator"> grain 激活器。</param>
        /// <returns>表示是否合适的值 grain 找到了活动器 。</returns>
        bool TryGet(GrainType grainType, GrainInterfaceType interfaceType, [NotNullWhen(true)] out IGrainReferenceActivator? activator);
    }

    /// <summary>
    /// 创建 grain 引用。
    /// </summary>
    public interface IGrainReferenceActivator
    {
        /// <summary>
        /// 创建新的 grain 引用。
        /// </summary>
        /// <param name="grainId">下 grain id. 数据。</param>
        /// <returns>新的 grain 参考。</returns>
        public GrainReference CreateReference(GrainId grainId);
    }
}

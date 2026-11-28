using System;
using System.Collections.Generic;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Reflection;
using Orleans.Serialization.Activators;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization.Configuration
{
    /// <summary>
    /// 代码生成器所知的所有类型的配置。
    /// </summary>
    public sealed class TypeManifestOptions
    {
#if NET5_0_OR_GREATER
        private const DynamicallyAccessedMemberTypes ImplementationTypeMembers =
            DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces;

        private const DynamicallyAccessedMemberTypes InterfaceTypeMembers =
            DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.NonPublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces;
#endif

        private readonly HashSet<Type> _activators = new();
        private readonly HashSet<Type> _fieldCodecs = new();
        private readonly HashSet<Type> _serializers = new();
        private readonly HashSet<Type> _copiers = new();
        private readonly HashSet<Type> _converters = new();
        private readonly HashSet<Type> _interfaces = new();
        private readonly HashSet<Type> _interfaceProxies = new();
        private readonly HashSet<Type> _interfaceImplementations = new();
        private readonly Dictionary<Type, InterfaceDiscoveryKinds> _interfaceDiscoveryRequests = new();
        private int _nextContractRegistrationOrder;
        // 访问可变集合意味着请求使用旧版发现机制，包括通过保留的集合引用添加的类型。
        private InterfaceDiscoveryKinds _accessedCollections;
        internal Dictionary<Type, List<SerializationContract>> SerializerContracts { get; } = new();
        internal Dictionary<Type, List<SerializationContract>> CopierContracts { get; } = new();
        internal Dictionary<Type, List<SerializationContract>> ActivatorContracts { get; } = new();
        internal Dictionary<Type, List<SerializationContract>> ConverterContracts { get; } = new();

        internal Dictionary<Type, Func<ICodecProvider, IFieldCodec>> CodecFactories { get; } = new();
        internal Dictionary<Type, Func<ICodecProvider, IDeepCopier>> CopierFactories { get; } = new();
        internal Dictionary<Type, Func<ICodecProvider, object>> SerializerServiceFactories { get; } = new();
        internal HashSet<Type> ContextTypes { get; } = new();

        /// <summary>
        /// 为封闭类型注册静态构造的序列化和拷贝实现。
        /// </summary>
        /// <typeparam name="T">序列化的类型。</typeparam>
        /// <param name="codecFactory">字段编解码器工厂。</param>
        /// <param name="copierFactory">深拷贝器工厂。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内缓存构造完成的编解码器和拷贝器实例，
        /// 并以封闭服务类型为键。因此工厂结果及其依赖项的生命周期与提供程序相同，
        /// 包括从瞬态或作用域依赖注入注册中获取的实例。
        /// 实现必须支持在并发序列化和拷贝操作中复用。
        /// </remarks>
        public void AddSerializer<T>(
            Func<ICodecProvider, IFieldCodec<T>> codecFactory,
            Func<ICodecProvider, IDeepCopier<T>> copierFactory)
        {
            if (codecFactory is null) throw new ArgumentNullException(nameof(codecFactory));
            if (copierFactory is null) throw new ArgumentNullException(nameof(copierFactory));
            CodecFactories.TryAdd(typeof(T), static provider =>
                Orleans.Serialization.GeneratedCodeHelpers.OrleansGeneratedCodeHelper.GetService<IFieldCodec<T>>(null!, provider));
            CopierFactories.TryAdd(typeof(T), static provider =>
                Orleans.Serialization.GeneratedCodeHelpers.OrleansGeneratedCodeHelper.GetService<IDeepCopier<T>>(null!, provider));
            SerializerServiceFactories.TryAdd(typeof(IFieldCodec<T>), codecFactory);
            SerializerServiceFactories.TryAdd(typeof(IDeepCopier<T>), copierFactory);
            ContextTypes.Add(typeof(T));
        }

        /// <summary>
        /// 注册生成的序列化器和拷贝器使用的静态构造服务。
        /// </summary>
        /// <typeparam name="TService">封闭服务类型。</typeparam>
        /// <param name="factory">服务工厂。</param>
        /// <remarks>
        /// 编解码器提供程序为每个服务类型构造并缓存一个实例。递归的生成代码
        /// 构造函数通过生成代码辅助工具保留对正在构造的依赖项的引用。
        /// 对于每种服务类型，使用首个注册。
        /// 缓存实例及其依赖项具有 <see cref="CodecProvider"/> 的生命周期，包括
        /// 从瞬态或作用域依赖注入注册中获取的实例。服务必须
        /// 支持序列化实现的并发使用。
        /// 在对象图构造期间，通过提供程序拥有的服务及这些封闭类型
        /// 工厂解析依赖项。通过显式构造函数参数或返回已捕获实例的工厂
        /// 提供外部依赖项。对象图发布后恢复常规依赖注入。
        /// </remarks>
        public void AddSerializerService<TService>(Func<ICodecProvider, TService> factory) where TService : class
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            SerializerServiceFactories.TryAdd(typeof(TService), factory);
        }

        /// <summary>
        /// 获取或设置是否应启用 <see cref="SerializerConfigurationAnalyzer"/>。
        /// </summary>
        /// <remarks>
        /// 此属性不会导致调用 <see cref="SerializerConfigurationAnalyzer"/>。
        /// 调用它是使用方框架的责任。
        /// </remarks>
        public bool? EnableConfigurationAnalysis { get; set; }

        /// <summary>
        /// 获取已知激活器集合；激活器负责创建给定类型的实例。
        /// </summary>
        public HashSet<Type> Activators
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve activator members required by trimming. "
                + "Use AddActivator(Type) when registering activators.")]
#endif
            get
            {
                _accessedCollections |= InterfaceDiscoveryKinds.Activator;
                return _activators;
            }
        }

        /// <summary>
        /// 获取已知字段编解码器集合；它们负责序列化及反序列化给定类型的字段。
        /// </summary>
        public HashSet<Type> FieldCodecs
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve field codec members required by trimming. "
                + "Use AddFieldCodec(Type) when registering field codecs.")]
#endif
            get
            {
                _accessedCollections |= InterfaceDiscoveryKinds.Serializer;
                return _fieldCodecs;
            }
        }

        /// <summary>
        /// 获取已知序列化器集合；它们负责序列化及反序列化给定类型。
        /// </summary>
        public HashSet<Type> Serializers
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve serializer members required by trimming. "
                + "Use AddSerializer(Type) when registering serializers.")]
#endif
            get
            {
                _accessedCollections |= InterfaceDiscoveryKinds.Serializer;
                return _serializers;
            }
        }

        /// <summary>
        /// 获取拷贝器集合；它们负责创建给定类型的深拷贝。
        /// </summary>
        public HashSet<Type> Copiers
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve copier members required by trimming. "
                + "Use AddCopier(Type) when registering copiers.")]
#endif
            get
            {
                _accessedCollections |= InterfaceDiscoveryKinds.Copier;
                return _copiers;
            }
        }

        /// <summary>
        /// 获取转换器集合；它们负责在类型之间进行转换。
        /// </summary>
        public HashSet<Type> Converters
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve converter members required by trimming. "
                + "Use AddConverter(Type) when registering converters.")]
#endif
            get
            {
                _accessedCollections |= InterfaceDiscoveryKinds.Converter;
                return _converters;
            }
        }

        /// <summary>
        /// 获取已知接口集合，这些接口在 <see cref="InterfaceProxies"/> 集合中具有对应代理。
        /// </summary>
        public HashSet<Type> Interfaces
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve interface members required by trimming. "
                + "Use AddInterface(Type) when registering interfaces.")]
#endif
            get => _interfaces;
        }

        /// <summary>
        /// 获取已知接口代理集合；这些代理捕获方法调用，使其可被序列化、反序列化，并在该接口的实现上调用。
        /// </summary>
        /// <remarks>
        /// 这样可将调用方与目标解耦，从而通过捕获、传输调用，再在目标对象上调用它，实现远程过程调用。
        /// </remarks>
        public HashSet<Type> InterfaceProxies
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve proxy members required by trimming. "
                + "Use AddInterfaceProxy(Type) when registering interface proxies.")]
#endif
            get => _interfaceProxies;
        }

        /// <summary>
        /// 获取接口实现集合，这些实现对应 <see cref="Interfaces"/> 中的接口。
        /// </summary>
        public HashSet<Type> InterfaceImplementations
        {
#if NET5_0_OR_GREATER
            [RequiresUnreferencedCode(
                "Direct collection access cannot preserve implementation members required by trimming. "
                + "Use AddInterfaceImplementation(Type) when registering interface implementations.")]
#endif
            get => _interfaceImplementations;
        }

        /// <summary>
        /// 获取已知类型标识符到对应类型的映射。
        /// </summary>
        public Dictionary<uint, Type> WellKnownTypeIds { get; } = new Dictionary<uint, Type>();

        /// <summary>
        /// 获取已知类型别名到对应类型的映射。
        /// </summary>
        public Dictionary<string, Type> WellKnownTypeAliases { get; } = new Dictionary<string, Type>();

        /// <summary>
        /// 获取允许的 Orleans 格式运行时类型名称集合。
        /// </summary>
        /// <remarks>
        /// <para>
        /// Orleans 格式的运行时类型名称使用 CLR 类型名称语法，包括命名空间、
        /// 嵌套、泛型实参、数组及可选的程序集限定信息。使用
        /// <see cref="RuntimeTypeNameFormatter.Format(Type)"/> 生成此格式。
        /// </para>
        /// <para>
        /// 优先使用 <see cref="AddAllowedType(Type)"/>（当 <see cref="Type"/> 可用时）。它生成
        /// 不含复合别名的底层 CLR 名称，以便之后的别名配置不会
        /// 影响允许列表条目。为兼容性，诸如 <see cref="Type.FullName"/> 的非限定名称仍然
        /// 受支持。
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddSerializer(builder => builder.Configure(options =>
        /// {
        ///     // 首选：由 Orleans 构造条目。
        ///     options.AddAllowedType(typeof(MyMessage));
        ///
        ///     // 需要字符串条目时也支持此方式。
        ///     options.AllowedTypes.Add(
        ///         RuntimeTypeNameFormatter.Format(typeof(MyOtherMessage)));
        /// }));
        /// </code>
        /// </example>
        public HashSet<string> AllowedTypes { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 获取其类型被允许的程序集名称集合。
        /// </summary>
        /// <remarks>
        /// 当程序集可用时，优先使用 <see cref="AddAllowedAssembly(Assembly)"/>，避免
        /// 手动构造程序集名称。
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddSerializer(builder => builder.Configure(options =>
        ///     options.AddAllowedAssembly(typeof(MyMessage).Assembly)));
        /// </code>
        /// </example>
        public HashSet<string> AllowedAssemblies { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 获取从复合类型别名到类型的映射。
        /// </summary>
        public CompoundTypeAliasTree CompoundTypeAliases { get; } = CompoundTypeAliasTree.Create();

        /// <summary>
        /// 获取或设置是否默认允许所有类型。
        /// 默认值：<see langword="false"/>。
        /// </summary>
        /// <remarks>
        /// 将此属性设置为 <see langword="true"/> 会绕过类型名称验证，允许任何
        /// 可解析的类型。如果不可信方可以影响序列化输入，则这样做不安全。
        /// 优先允许单个类型或可信程序集。
        /// </remarks>
        public bool AllowAllTypes { get; set; }

        /// <summary>
        /// 获取已配置此实例的类型清单提供程序集合。
        /// </summary>
        internal HashSet<object> TypeManifestProviders { get; } = new();

        internal HashSet<Type> ActivatorTypes => _activators;

        internal HashSet<Type> FieldCodecTypes => _fieldCodecs;

        internal HashSet<Type> SerializerTypes => _serializers;

        internal HashSet<Type> CopierTypes => _copiers;

        internal HashSet<Type> ConverterTypes => _converters;

        internal HashSet<Type> InterfaceTypes => _interfaces;

        internal HashSet<Type> InterfaceProxyTypes => _interfaceProxies;

        internal HashSet<Type> InterfaceImplementationTypes => _interfaceImplementations;

        internal bool DiscoverInterfaces(Type type, Type contractType)
        {
            var kind = contractType == typeof(IFieldCodec<>) || contractType == typeof(IBaseCodec<>) || contractType == typeof(IValueSerializer<>)
                ? InterfaceDiscoveryKinds.Serializer
                : contractType == typeof(IDeepCopier<>) || contractType == typeof(IBaseCopier<>)
                    ? InterfaceDiscoveryKinds.Copier
                    : contractType == typeof(IActivator<>)
                        ? InterfaceDiscoveryKinds.Activator
                        : InterfaceDiscoveryKinds.Converter;

            _interfaceDiscoveryRequests.TryGetValue(type, out var requests);
            return ((_accessedCollections | requests) & kind) != 0;
        }

        /// <summary>
        /// 添加序列化器实现类型，并保留用于检查及激活它的成员。
        /// </summary>
        /// <param name="type">序列化器实现类型。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddSerializer(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_serializers, InterfaceDiscoveryKinds.Serializer, type);

        /// <summary>
        /// 添加字段编解码器实现类型，并保留用于检查及激活它的成员。
        /// </summary>
        /// <param name="type">字段编解码器实现类型。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddFieldCodec(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_fieldCodecs, InterfaceDiscoveryKinds.Serializer, type);

        /// <summary>
        /// 添加拷贝器实现类型，并保留用于检查及激活它的成员。
        /// </summary>
        /// <param name="type">拷贝器实现类型。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddCopier(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_copiers, InterfaceDiscoveryKinds.Copier, type);

        /// <summary>
        /// 添加转换器实现类型，并保留用于检查及激活它的成员。
        /// </summary>
        /// <param name="type">转换器实现类型。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddConverter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_converters, InterfaceDiscoveryKinds.Converter, type);

        /// <summary>
        /// 添加激活器实现类型，并保留用于检查及激活它的成员。
        /// </summary>
        /// <param name="type">激活器实现类型。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddActivator(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_activators, InterfaceDiscoveryKinds.Activator, type);

        private void AddImplementation(HashSet<Type> types, InterfaceDiscoveryKinds kind, Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            types.Add(type);
            _interfaceDiscoveryRequests.TryGetValue(type, out var requests);
            _interfaceDiscoveryRequests[type] = requests | kind;
        }

        /// <summary>
        /// 为字段编解码器的目标类型注册该编解码器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">编解码器实现类型。</param>
        /// <param name="targetType">序列化的类型，或者在开放泛型编解码器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddSerializer(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_serializers, SerializerContracts, type, targetType, typeof(IFieldCodec<>));

        /// <summary>
        /// 为字段编解码器的目标类型注册该编解码器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">编解码器实现类型。</param>
        /// <param name="targetType">序列化的类型，或者在开放泛型编解码器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddFieldCodec(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_fieldCodecs, SerializerContracts, type, targetType, typeof(IFieldCodec<>));

        /// <summary>
        /// 为基类型编解码器的目标类型注册该编解码器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">基类型编解码器实现类型。</param>
        /// <param name="targetType">序列化的类型，或者在开放泛型编解码器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddBaseCodec(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_serializers, SerializerContracts, type, targetType, typeof(IBaseCodec<>));

        /// <summary>
        /// 为值序列化器的目标类型注册该序列化器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">值序列化器实现类型。</param>
        /// <param name="targetType">序列化的类型，或者在开放泛型序列化器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddValueSerializer(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_serializers, SerializerContracts, type, targetType, typeof(IValueSerializer<>));

        /// <summary>
        /// 为深拷贝器的目标类型注册该拷贝器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">拷贝器实现类型。</param>
        /// <param name="targetType">被拷贝的类型，或者在开放泛型拷贝器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddCopier(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_copiers, CopierContracts, type, targetType, typeof(IDeepCopier<>));

        /// <summary>
        /// 为基类型拷贝器的目标类型注册该拷贝器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">基类型拷贝器实现类型。</param>
        /// <param name="targetType">被拷贝的类型，或者在开放泛型拷贝器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddBaseCopier(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_copiers, CopierContracts, type, targetType, typeof(IBaseCopier<>));

        /// <summary>
        /// 为激活器的目标类型注册该激活器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">激活器实现类型。</param>
        /// <param name="targetType">被激活的类型，或者在开放泛型激活器情况下为其泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddActivator(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType) => AddContract(_activators, ActivatorContracts, type, targetType, typeof(IActivator<>));

        /// <summary>
        /// 为转换器的原始类型及代理类型注册该转换器，并保留其激活及目标接口元数据。
        /// </summary>
        /// <param name="type">转换器实现类型。</param>
        /// <param name="targetType">被转换值的类型，或者在开放泛型转换器情况下为其泛型定义。</param>
        /// <param name="surrogateType">代理类型，或以相同顺序使用转换器实参的泛型定义。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// </remarks>
        public void AddConverter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type surrogateType)
        {
            if (surrogateType is null)
            {
                throw new ArgumentNullException(nameof(surrogateType));
            }

            AddContract(_converters, ConverterContracts, type, targetType, typeof(IConverter<,>), surrogateType);
        }

        /// <summary>
        /// 注册使用其泛型参数描述代理类型的转换器。
        /// </summary>
        /// <param name="type">转换器实现类型。</param>
        /// <param name="targetType">被转换的类型或其泛型定义。</param>
        /// <param name="surrogateType">代理类型描述，将参数绑定到转换器实现。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// <para>
        /// 可执行代理类型描述中的数组节点必须是源码中已知的封闭类型，
        /// 并通过 <see cref="SerializationType.Create"/> 提供。使用接受具体代理类型的
        /// 重载直接注册封闭转换器及其代理类型。
        /// </para>
        /// </remarks>
        public void AddConverter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type targetType,
            SerializationType surrogateType)
        {
            if (surrogateType is null)
            {
                throw new ArgumentNullException(nameof(surrogateType));
            }

            AddContract(_converters, ConverterContracts, type, targetType, typeof(IConverter<,>), surrogateDescription: surrogateType);
        }

        /// <summary>
        /// 注册以实现的泛型参数描述目标的序列化契约。
        /// </summary>
        /// <param name="type">实现类型。</param>
        /// <param name="contractType">字段编解码器、基类型编解码器、值序列化器、拷贝器、激活器或转换器接口的泛型定义。</param>
        /// <param name="targetType">目标类型描述。</param>
        /// <param name="surrogateType">转换器契约的代理类型描述。</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 在其生命周期内按封闭实现类型缓存已完成构造的实例。
        /// 实现及其依赖项具有提供程序的生命周期，包括从
        /// 瞬态或作用域依赖注入注册中解析的实例，并且必须支持并发使用。
        /// <para>
        /// 目标描述支持结构化数组匹配。可执行代理类型描述
        /// 要求通过 <see cref="SerializationType.Create"/> 提供源码中已知的封闭数组类型。
        /// 对于匹配的数组目标，已注册的转换器先于内置数组编解码器和
        /// 拷贝器后备实现处理。直接注册的编解码器和拷贝器仍优先于转换器。
        /// </para>
        /// </remarks>
        public void AddSerializationContract(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type type,
            Type contractType,
            SerializationType targetType,
            SerializationType? surrogateType = null)
        {
            if (targetType is null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var (types, contracts) = contractType == typeof(IFieldCodec<>) || contractType == typeof(IBaseCodec<>) || contractType == typeof(IValueSerializer<>)
                ? (_serializers, SerializerContracts)
                : contractType == typeof(IDeepCopier<>) || contractType == typeof(IBaseCopier<>)
                    ? (_copiers, CopierContracts)
                    : contractType == typeof(IActivator<>)
                        ? (_activators, ActivatorContracts)
                        : contractType == typeof(IConverter<,>)
                            ? (_converters, ConverterContracts)
                            : throw new ArgumentException("The contract must be a supported serialization interface definition.", nameof(contractType));
            if ((contractType == typeof(IConverter<,>)) != (surrogateType is not null))
            {
                throw new ArgumentException("Only converter contracts require a surrogate description.", nameof(surrogateType));
            }

            AddContract(types, contracts, type, null, contractType,
                surrogateDescription: surrogateType, targetDescription: targetType);
        }

        private void AddContract(
            HashSet<Type> types,
            Dictionary<Type, List<SerializationContract>> contracts,
            Type type,
            Type? targetType,
            Type contractType,
            Type? surrogateType = null,
            SerializationType? surrogateDescription = null,
            SerializationType? targetDescription = null)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (targetType is null && targetDescription is null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            types.Add(type);
            if (!contracts.TryGetValue(type, out var registrations))
            {
                contracts[type] = registrations = new();
            }

            var registration = new SerializationContract(contractType, targetType, surrogateType, surrogateDescription, targetDescription);
            if (!registrations.Exists(existing => existing.IsEquivalentTo(registration)))
            {
                registrations.Add(registration with { RegistrationOrder = _nextContractRegistrationOrder++ });
            }
        }

        /// <summary>
        /// 添加生成的接口类型，并保留生成的可调用对象所使用的方法及继承的接口。
        /// </summary>
        /// <param name="type">生成的接口类型。</param>
        public void AddInterface(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(InterfaceTypeMembers)]
#endif
            Type type) => _interfaces.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 添加生成的代理类型，并保留其实现的接口。
        /// </summary>
        /// <param name="type">生成的代理类型。</param>
        public void AddInterfaceProxy(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type type) => _interfaceProxies.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 添加生成的接口实现类型，并保留其公共构造函数及实现的接口。
        /// </summary>
        /// <param name="type">生成的接口实现类型。</param>
        public void AddInterfaceImplementation(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => _interfaceImplementations.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 将 <paramref name="type"/> 的 Orleans 格式运行时类型名称添加到
        /// <see cref="AllowedTypes"/>.
        /// </summary>
        /// <remarks>
        /// 这是允许已取得的 <see cref="Type"/> 的首选方式。它格式化底层
        /// CLR 类型名称，不含复合别名，并包含所有已构造的泛型组成部分。
        /// </remarks>
        /// <param name="type">要允许的类型。</param>
        public void AddAllowedType(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            AllowedTypes.Add(RuntimeTypeNameFormatter.FormatInternalNoCache(type, allowAliases: false));
        }

        /// <summary>
        /// 将 <paramref name="assembly"/> 的程序集名称添加到 <see cref="AllowedAssemblies"/>。
        /// </summary>
        /// <param name="assembly">要允许的程序集。</param>
        public void AddAllowedAssembly(Assembly assembly)
        {
            if (assembly is null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            AllowedAssemblies.Add(CachedTypeResolver.GetName(assembly));
        }

        [Flags]
        private enum InterfaceDiscoveryKinds
        {
            Serializer = 1,
            Copier = 2,
            Activator = 4,
            Converter = 8
        }
    }
}

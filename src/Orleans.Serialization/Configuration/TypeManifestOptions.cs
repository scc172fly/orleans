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
    /// 代码生成器已知的所有类型的配置 。
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
        // 监听式收藏访问请求发现遗留问题,包括通过保留收集参考资料添加的类型。
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
        /// 登记固定结构的序列和复制执行封闭型号。
        /// </summary>
        /// <typeparam name="T">序列型号</typeparam>
        /// <param name="codecFactory">实地编码工厂。</param>
        /// <param name="copierFactory">深层复印机的工厂</param>
        /// <remarks>
        /// 每个 <see cref="CodecProvider"/> 都在其整个生命周期内隐藏完成的编码器和复印机
        /// 工厂结果及其依赖性因此具有供应商寿命,
        /// 包括从临时或范围化的自有注射登记中获取的实例。
        /// 执行工作必须支持在同时进行的序列和复制作业中重新使用。
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
        /// 登记了由生成的序列器和复印机使用的静态结构服务。
        /// </summary>
        /// <typeparam name="TService">封闭式服务类型。</typeparam>
        /// <param name="factory">服务工厂</param>
        /// <remarks>
        /// 代码化提供者构建并隐藏每个服务类型一例的缓存。
        /// 建筑商通过生成编码助手保留提及在建工程依赖的文字。
        /// 使用一种服务类型的首次登记。
        /// 紧凑的事例及其依赖性具有 <see cref="CodecProvider"/> 寿命,包括
        /// 临时或范围化的自有注射登记中获取的文献。
        /// 通过执行序列化支持同时使用。
        /// 在图示施工期间,依赖关系通过供应商拥有的服务解决,这些已关闭
        /// 通过明确的建筑师论点或
        /// 返回捕捉到的例。 在图表发布后, 恢复正常依赖注射。
        /// </remarks>
        public void AddSerializerService<TService>(Func<ICodecProvider, TService> factory) where TService : class
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            SerializerServiceFactories.TryAdd(typeof(TService), factory);
        }

        /// <summary>
        /// 获取或设置一个值,以显示是否应该启用 <see cref="SerializerConfigurationAnalyzer"/> 。
        /// </summary>
        /// <remarks>
        /// 此财产不导致 <see cref="SerializerConfigurationAnalyzer"/> 被援引。
        /// 这是消费框架的责任。
        /// </remarks>
        public bool? EnableConfigurationAnalysis { get; set; }

        /// <summary>
        /// 获取一组已知的活性者,这些能负责创建特定类型的实例。
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
        /// 获取一套已知的字段编码器,这些编码器负责对某一类型域进行序列和取消序列。
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
        /// 获取一组已知的序列器,这些序列器负责对某一类型进行序列和取消其序列。
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
        /// 获得一组复印机,负责制作某种类型的深度拷贝。
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
        /// 获取一组转换器,这些转换器负责从一种类型转换成另一种类型。
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
        /// 获取一组已知的接口,即 <see cref="InterfaceProxies"/> 收藏中具有相应代理的界面。
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
        /// 获得一套已知界面代理,该代理可捕捉可以序列化、消毒和针对实施这一接口而援引的可捕捉方法职业。
        /// </summary>
        /// <remarks>
        /// 这样可以将呼叫者和目标分离,从而通过捕获、传送和随后针对目标物体援引远程程序呼叫,实施远程程序呼叫。
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
        /// 获取一组接口执行,即 <see cref="Interfaces"/>中存在的接口执行。
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
        /// 将已知类型识别特征的映射图与相应的类型相匹配。
        /// </summary>
        public Dictionary<uint, Type> WellKnownTypeIds { get; } = new Dictionary<uint, Type>();

        /// <summary>
        /// 将已知型号化名的映射图 变为相应的类型
        /// </summary>
        public Dictionary<string, Type> WellKnownTypeAliases { get; } = new Dictionary<string, Type>();

        /// <summary>
        /// 获取允许的 Orleans-格式化运行时间类型名称组。
        /// </summary>
        /// <remarks>
        /// <para>
        /// Orleans- 格式化运行时间类型名称使用 CLR 类型名语法,包括命名空间
        /// 嵌套、通用参数、阵列和可选组装资格。使用
        /// 要制作此格式,<see cref="RuntimeTypeNameFormatter.Format(Type)"/> 。
        /// </para>
        /// <para>
        /// 优 <see cref="AddAllowedType(Type)"/> 时 <see cref="Type"/> 生产
        /// 底部的 CLR 名称没有复合别名, 以便以后的别名配置不会
        /// 允许列表条目影响允许列表项。未资格名称,例如 <see cref="Type.FullName"/>
        /// 支持兼容性。
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddSerializer(builder => builder.Configure(options =>
        /// {
        ///     // 首选方式是让 Orleans 构造条目。
        ///     options.AddAllowedType(typeof(MyMessage));
        ///
        ///     // 需要字符串条目时可使用此方式。
        ///     options.AllowedTypes.Add(
        ///         RuntimeTypeNameFormatter.Format(typeof(MyOtherMessage)));
        /// }));
        /// </code>
        /// </example>
        public HashSet<string> AllowedTypes { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 获取允许类型组装的集合名称。
        /// </summary>
        /// <remarks>
        /// 预选 <see cref="AddAllowedAssembly(Assembly)"/> ,当装配组装可以避免
        /// 手工构建组装名称 。
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddSerializer(builder => builder.Configure(options =>
        ///     options.AddAllowedAssembly(typeof(MyMessage).Assembly)));
        /// </code>
        /// </example>
        public HashSet<string> AllowedAssemblies { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 将复合型别名的绘图转换成类型。
        /// </summary>
        public CompoundTypeAliasTree CompoundTypeAliases { get; } = CompoundTypeAliasTree.Create();

        /// <summary>
        /// 获取或设置一个值,以显示是否默认允许所有类型。
        /// 默认: <see langword="false"/>。
        /// </summary>
        /// <remarks>
        /// 设置此属性为 <see langword="true"/> ,绕过类型名称验证并允许
        /// 不可解析的类型。 当序列输入可能受到不信任方的影响时, 这不安全 。
        /// 更可取的做法是允许个别类型或信任的集会。
        /// </remarks>
        public bool AllowAllTypes { get; set; }

        /// <summary>
        /// 获取配置此实例的一组类型列表提供者。
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
        /// 添加序列执行类型,并保存成员用来检查和激活该类型。
        /// </summary>
        /// <param name="type">序列仪执行类型。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// </remarks>
        public void AddSerializer(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_serializers, InterfaceDiscoveryKinds.Serializer, type);

        /// <summary>
        /// 添加字段代码执行类型,并保存用于检查和激活该功能的成员。
        /// </summary>
        /// <param name="type">外地编码执行类型。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// </remarks>
        public void AddFieldCodec(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_fieldCodecs, InterfaceDiscoveryKinds.Serializer, type);

        /// <summary>
        /// 增加复印机执行类型,并保存用于检查和激活该机的成员。
        /// </summary>
        /// <param name="type">复印机执行类型。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// </remarks>
        public void AddCopier(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_copiers, InterfaceDiscoveryKinds.Copier, type);

        /// <summary>
        /// 添加转换器执行类型,并保存用于检查和激活该转换器的成员。
        /// </summary>
        /// <param name="type">转换器执行类型。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// </remarks>
        public void AddConverter(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => AddImplementation(_converters, InterfaceDiscoveryKinds.Converter, type);

        /// <summary>
        /// 添加一个活动实施类型,并保存成员用来检查和激活该类型。
        /// </summary>
        /// <param name="type">激活器执行类型。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记目标类型的字段编码,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">代码执行类型。</param>
        /// <param name="targetType">序列型号,或其通用的开放式通用编码定义。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记目标类型的字段编码,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">代码执行类型。</param>
        /// <param name="targetType">序列型号,或其通用的开放式通用编码定义。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记目标类型的基本编码,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">基本编码执行类型。</param>
        /// <param name="targetType">序列型号,或其通用的开放式通用编码定义。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记其目标类型的价值序列器,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">值序列器执行类型。</param>
        /// <param name="targetType">序列型号,或其通用的开放式通用序列器定义。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记其目标类型的深复印机,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">复印机执行类型。</param>
        /// <param name="targetType">复制型号,或其通用定义的开放通用复印机。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记目标类型的基本复印机,并保存其启动和目标接口元数据。
        /// </summary>
        /// <param name="type">基础复印机执行类型。</param>
        /// <param name="targetType">复制型号,或其通用定义的开放通用复印机。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 登记目标类型活动的启动者,并保存其激活和目标接口元数据。
        /// </summary>
        /// <param name="type">激活器执行类型。</param>
        /// <param name="targetType">激活型号,或其通用定义的开放通用引爆器。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 注册转换器,以记录其价值和代用类型,并保存其激活和目标接口元数据。
        /// </summary>
        /// <param name="type">转换器执行类型。</param>
        /// <param name="targetType">转换值类型,或开放通用换算器的通用定义。</param>
        /// <param name="surrogateType">代位类型,或使用转换器的同顺序参数的一般定义。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
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
        /// 注册一个转换器,其代用类型使用通用参数加以描述。
        /// </summary>
        /// <param name="type">转换器执行类型。</param>
        /// <param name="targetType">转换型号或通用定义。</param>
        /// <param name="surrogateType">代名词描述,对转换器执行的约束参数。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// <para>
        /// 执行可执行代理描述中使用的矩形结点必须是源已知的封闭型
        /// 通过 <see cref="SerializationType.Create"/>供应的。使用混凝土替代型号
        /// 超载以注册关闭的转换器及其代理器。
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
        /// 登记了使用执行通用参数说明目标的序列化合同。
        /// </summary>
        /// <param name="type">执行类型。</param>
        /// <param name="contractType">外地编码、基本编码、价值序列器、复印机、活动器或转换器接口的通用定义。</param>
        /// <param name="targetType">目标类型描述。</param>
        /// <param name="surrogateType">转换器合同的代用说明。</param>
        /// <remarks>
        /// 每一 <see cref="CodecProvider"/> 在其整个寿命期内,通过封闭式实施类型完成了缓存。
        /// 执行及其依赖性具有提供方的寿命,包括从实施到获得解决的情况
        /// 瞬时或范围较广的自有注射登记,必须支持同时使用。
        /// <para>
        /// 目标描述支持结构阵列匹配。 可执行代数描述
        /// 需要通过 <see cref="SerializationType.Create"/>提供源已知的封闭式阵列类型。
        /// 注册的转换器在内置数组编码和
        /// 直接注册的编码器和复印机保留相对于转换器的优先权。
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
        /// 添加生成的界面类型,并保存生成的可引用物所使用的方法和遗留界面。
        /// </summary>
        /// <param name="type">生成的接口类型。</param>
        public void AddInterface(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(InterfaceTypeMembers)]
#endif
            Type type) => _interfaces.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 添加生成的代理类型并保存其已安装的接口。
        /// </summary>
        /// <param name="type">生成的代理类型。</param>
        public void AddInterfaceProxy(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
#endif
            Type type) => _interfaceProxies.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 添加生成的接口执行类型,并保存其公共构筑器和实施的接口。
        /// </summary>
        /// <param name="type">生成的接口执行类型。</param>
        public void AddInterfaceImplementation(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(ImplementationTypeMembers)]
#endif
            Type type) => _interfaceImplementations.Add(type ?? throw new ArgumentNullException(nameof(type)));

        /// <summary>
        /// 添加 Orleans- <paramref name="type"/> 格式化运行时间类型名称
        /// <see cref="AllowedTypes"/>.
        /// </summary>
        /// <remarks>
        /// 这是允许可用 <see cref="Type"/>的首选方法。 它以基本
        /// CLR型号没有复合别名,包括所有已建造的通用部件。
        /// </remarks>
        /// <param name="type">允许的类型。</param>
        public void AddAllowedType(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            AllowedTypes.Add(RuntimeTypeNameFormatter.FormatInternalNoCache(type, allowAliases: false));
        }

        /// <summary>
        /// 将 <paramref name="assembly"/> 的组装名称添加到 <see cref="AllowedAssemblies"/>。
        /// </summary>
        /// <param name="assembly">允许的组装。</param>
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

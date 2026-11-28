using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Serialization.Configuration;

namespace Orleans.Configuration
{
    /// <summary>
    /// 包含 Grain 类型描述。
    /// </summary>
    public class GrainTypeOptions
    {
        private readonly HashSet<Type> _classes = new();

        /// <summary>
        /// 获取 Grain 类的元数据集合。
        /// </summary>
        /// <remarks>
        /// 使用 <see cref="AddClass(Type)"/> 注册 Grain 类，并保留其用于激活的公共构造函数。
        /// </remarks>
        public HashSet<Type> Classes
        {
            [RequiresUnreferencedCode("Direct collection access requires grain public constructors to be preserved separately. Use AddClass(Type) when registering grain classes.")]
            get => _classes;
        }

        /// <summary>
        /// 添加 Grain 类，并保留其公共构造函数和已实现的接口以供激活使用。
        /// </summary>
        /// <param name="grainClass">Grain 实现类。</param>
        /// <exception cref="ArgumentNullException"><paramref name="grainClass"/> 为 <see langword="null"/>。</exception>
        public void AddClass(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] Type grainClass)
            => _classes.Add(grainClass ?? throw new ArgumentNullException(nameof(grainClass)));

        internal HashSet<Type> GrainClasses => _classes;

        /// <summary>
        /// 获取 Grain 接口的元数据集合。
        /// </summary>
        public HashSet<Type> Interfaces { get; } = new();
    }

    /// <summary>
    /// <see cref="GrainTypeOptions"/> 的默认配置提供程序。
    /// </summary>
    internal sealed class DefaultGrainTypeOptionsProvider : IConfigureOptions<GrainTypeOptions>
    {
        private readonly TypeManifestOptions _typeManifestOptions;

        /// <summary>
        /// 初始化 <see cref="DefaultGrainTypeOptionsProvider"/> 类的新实例。
        /// </summary>
        /// <param name="typeManifestOptions">类型清单选项。</param>
        public DefaultGrainTypeOptionsProvider(IOptions<TypeManifestOptions> typeManifestOptions) => _typeManifestOptions = typeManifestOptions.Value;

        /// <inheritdoc />
        public void Configure(GrainTypeOptions options)
        {
            foreach (var type in _typeManifestOptions.InterfaceTypes)
            {
                if (typeof(IAddressable).IsAssignableFrom(type))
                {
                    options.Interfaces.Add(type);
                }
            }

            foreach (var type in _typeManifestOptions.InterfaceImplementationTypes)
            {
                if (IsImplementationType(type))
                {
                    options.GrainClasses.Add(type switch
                    {
                        { IsGenericType: true, IsConstructedGenericType: false } => type.GetGenericTypeDefinition(),
                        _ => type
                    });
                }
            }

            static bool IsImplementationType(Type type)
            {
                if (type.IsAbstract || type.IsInterface)
                {
                    return false;
                }

                if (typeof(IGrain).IsAssignableFrom(type))
                {
                    return true;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 验证 <see cref="GrainTypeOptions"/>。
    /// </summary>
    public sealed class GrainTypeOptionsValidator : IConfigurationValidator
    {
        private readonly IOptions<GrainTypeOptions> _options;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// 初始化 <see cref="GrainTypeOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        public GrainTypeOptionsValidator(IOptions<GrainTypeOptions> options, IServiceProvider serviceProvider)
        {
            _options = options;
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (_options.Value.Interfaces is not { Count: > 0 })
            {
                throw new OrleansConfigurationException($"No grain interfaces have been configured. Either add some grain interfaces and reference the Orleans.Sdk package, or remove {nameof(GrainTypeOptionsValidator)} from the services collection.");
            }

            var isSilo = _serviceProvider.GetService(typeof(ILocalSiloDetails)) != null;
            if (isSilo)
            {
                if (_options.Value.GrainClasses is not { Count: > 0 })
                {
                    throw new OrleansConfigurationException($"No grain classes have been configured. Either add some grain classes and reference the Orleans.Sdk package, or remove {nameof(GrainTypeOptionsValidator)} from the services collection.");
                }
            }
        }
    }
}

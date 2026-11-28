using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Metadata
{
    /// <summary>
    /// <see cref="GrainType"/> 与实现它的 <see cref="Type"/> 之间的映射。
    /// </summary>
    public class GrainClassMap
    {
        private readonly TypeConverter _typeConverter;
        private readonly ImmutableDictionary<GrainType, Type> _types;

        /// <summary>
        /// 初始化 <see cref="GrainClassMap"/> 类的新实例。
        /// </summary>
        /// <param name="typeConverter">类型转换器。</param>
        /// <param name="classes">
        /// 保留了公共构造函数的 Grain 类，注册方法为
        /// <see cref="Configuration.GrainTypeOptions.AddClass(Type)"/> 或
        /// <see cref="Serialization.Configuration.TypeManifestOptions.AddInterfaceImplementation(Type)"/>.
        /// </param>
        [RequiresUnreferencedCode("The dictionary's grain types must have public constructors preserved separately, for example by GrainTypeOptions.AddClass or TypeManifestOptions.AddInterfaceImplementation.")]
        public GrainClassMap(TypeConverter typeConverter, ImmutableDictionary<GrainType, Type> classes)
        {
            _typeConverter = typeConverter;
            _types = classes;
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "SiloManifestProvider builds this dictionary from GrainTypeOptions registrations. Generated manifests preserve constructors through AddInterfaceImplementation and manual registrations through AddClass. Direct Classes access warns callers to preserve constructors separately.")]
        internal static GrainClassMap CreateRegistered(TypeConverter typeConverter, ImmutableDictionary<GrainType, Type> classes)
            => new(typeConverter, classes);

        /// <summary>
        /// 返回与提供的 Grain 类型对应的 Grain 类类型。
        /// </summary>
        /// <param name="grainType">Grain 的类型。</param>
        /// <param name="grainClass">保留公共构造函数以供激活使用的 Grain 类。</param>
        /// <returns>如果找到对应的 Grain 类，则为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
        public bool TryGetGrainClass(
            GrainType grainType,
            [NotNullWhen(true), DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] out Type? grainClass)
        {
            GrainType lookupType;
            Type[]? args;
            if (GenericGrainType.TryParse(grainType, out var genericId))
            {
                lookupType = genericId.GetUnconstructedGrainType().GrainType;
                args = genericId.GetArguments(_typeConverter);
            }
            else
            {
                lookupType = grainType;
                args = default;
            }

            if (!TryGetRegisteredGrainClass(lookupType, out var registeredClass))
            {
                grainClass = null;
                return false;
            }

            grainClass = args is null ? registeredClass : MakeGenericGrainClass(registeredClass, args);

            return true;
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2067",
            Justification = "Generated and manual registrations preserve public constructors through TypeManifestOptions.AddInterfaceImplementation or GrainTypeOptions.AddClass. Direct Classes access and the public dictionary constructor require callers to preserve constructors separately and warn when trimming. The collections retain those types but cannot carry their annotations.")]
        private bool TryGetRegisteredGrainClass(
            GrainType grainType,
            [NotNullWhen(true), DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] out Type? grainClass)
            => _types.TryGetValue(grainType, out grainClass);

        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        private static Type MakeGenericGrainClass(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type grainClass,
            Type[] arguments)
            => grainClass.MakeGenericType(arguments);
    }
}

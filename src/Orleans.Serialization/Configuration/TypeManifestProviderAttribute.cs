using System;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace Orleans.Serialization.Configuration
{
    /// <summary>
    /// 定义此组件的元数据提供者。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeManifestProviderAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="TypeManifestProviderAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="providerType">元数据提供者类型。</param>
        public TypeManifestProviderAttribute(
#if NET5_0_OR_GREATER
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
            Type providerType)
        {
            if (providerType is null)
            {
                throw new ArgumentNullException(nameof(providerType));
            }

            if (!typeof(ITypeManifestProvider).IsAssignableFrom(providerType))
            {
                throw new ArgumentException($"Provided type {providerType} must implement {typeof(ITypeManifestProvider)}", nameof(providerType));
            }

            ProviderType = providerType;
        }

        /// <summary>
        /// 获得舱单提供者的类型。
        /// </summary>
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        public Type ProviderType { get; }
    }
}

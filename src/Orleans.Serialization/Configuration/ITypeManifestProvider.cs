using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Orleans.Serialization.Configuration
{
    /// <summary>
    /// 为由配置驱动的 Orleans 提供程序提供元数据。
    /// </summary>
    public interface IProviderMetadataProvider
    {
        /// <summary>
        /// 将已知的提供程序添加到 <paramref name="providers"/>。
        /// </summary>
        /// <param name="providers">提供程序注册项，以目标、种类和名称作为键。</param>
        void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers);
    }

    /// <summary>
    /// 提供类型清单信息。
    /// </summary>
    public interface ITypeManifestProvider : IConfigureOptions<TypeManifestOptions>
    {
    }

    /// <summary>
    /// 生成的类型清单提供程序的基类。
    /// </summary>
    public abstract class TypeManifestProviderBase : ITypeManifestProvider
    {
        /// <inheritdoc/>
        void IConfigureOptions<TypeManifestOptions>.Configure(TypeManifestOptions options)
        {
            if (options.TypeManifestProviders.Add(Key))
            {
                ConfigureInner(options);
            }
        }

        /// <summary>
        /// 获取此类型清单提供程序的唯一标识符。
        /// </summary>
        public virtual object Key => GetType();

        /// <summary>
        /// 配置给定的类型清单选项。
        /// </summary>
        /// <param name="options">类型清单选项。</param>
        protected abstract void ConfigureInner(TypeManifestOptions options);
    }
}

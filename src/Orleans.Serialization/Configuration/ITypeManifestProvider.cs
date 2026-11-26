using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Orleans.Serialization.Configuration
{
    /// <summary>
    /// 为配置驱动的 Orleans 提供者提供元数据。
    /// </summary>
    public interface IProviderMetadataProvider
    {
        /// <summary>
        /// 将已知的提供者添加到 <paramref name="providers"/>。
        /// </summary>
        /// <param name="providers">供应商登记,按目标、种类和名称键定。</param>
        void ConfigureProviders(IDictionary<(string Target, string Kind, string Name), Type> providers);
    }

    /// <summary>
    /// 提供类别清单信息。
    /// </summary>
    public interface ITypeManifestProvider : IConfigureOptions<TypeManifestOptions>
    {
    }

    /// <summary>
    /// 生成类型列表提供者的基础类 。
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
        /// 获取这类显示器提供者的独特身份识别符。
        /// </summary>
        public virtual object Key => GetType();

        /// <summary>
        /// 配置所提供的类型列表选项。
        /// </summary>
        /// <param name="options">类型列表选项。</param>
        protected abstract void ConfigureInner(TypeManifestOptions options);
    }
}

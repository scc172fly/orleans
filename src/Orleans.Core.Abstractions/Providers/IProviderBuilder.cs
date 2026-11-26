using Microsoft.Extensions.Configuration;

namespace Orleans.Providers;

/// <summary>
/// 配置 Orleans 服务的供应商的接口。
/// </summary>
/// <typeparam name="TBuilder">建造者的类型,如 <c>ISiloBuilder</c> 或 <c>IClientBuilder</c>。</typeparam>
public interface IProviderBuilder<TBuilder>
{
    /// <summary>
    /// 配置提供者。
    /// </summary>
    /// <param name="builder">建筑工。</param>
    /// <param name="name">提供商名称,或 <see langword="null"/> 如果未指定名称, 则该名称 。</param>
    /// <param name="configurationSection">配置部分包含供应商配置。</param>
    void Configure(TBuilder builder, string? name, IConfigurationSection configurationSection);
}

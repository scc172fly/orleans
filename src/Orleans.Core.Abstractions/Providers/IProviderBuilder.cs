using Microsoft.Extensions.Configuration;

namespace Orleans.Providers;

/// <summary>
/// 用于配置 Orleans 服务的提供程序接口。
/// </summary>
/// <typeparam name="TBuilder">构建器的类型，例如 <c>ISiloBuilder</c> 或 <c>IClientBuilder</c>。</typeparam>
public interface IProviderBuilder<TBuilder>
{
    /// <summary>
    /// 配置提供程序。
    /// </summary>
    /// <param name="builder">构建器。</param>
    /// <param name="name">提供程序名称；如果未指定名称，则为 <see langword="null"/>。</param>
    /// <param name="configurationSection">包含提供程序配置的配置节。</param>
    void Configure(TBuilder builder, string? name, IConfigurationSection configurationSection);
}

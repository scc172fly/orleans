using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Providers;

[assembly: RegisterProvider("Memory", "Streaming", "Client", typeof(MemoryStreamProviderBuilder))]
[assembly: RegisterProvider("Memory", "Streaming", "Silo", typeof(MemoryStreamProviderBuilder))]
namespace Orleans.Providers;

internal sealed class MemoryStreamProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        // 流动提供者根据指定的小节配置。
        builder.AddMemoryStreams(name!, stream => stream.ConfigureCache(options => options.Bind(configurationSection)));
    }

    public void Configure(IClientBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.AddMemoryStreams(name!); // 流动提供者根据指定的小节配置。
    }
}

using Orleans.Providers;
using Microsoft.Extensions.Configuration;
using Orleans;
using Orleans.Hosting;
using StackExchange.Redis;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

[assembly: RegisterProvider("Redis", "Streaming", "Silo", typeof(RedisStreamingProviderBuilder))]
[assembly: RegisterProvider("AzureRedisCache", "Streaming", "Silo", typeof(RedisStreamingProviderBuilder))]
[assembly: RegisterProvider("Redis", "Streaming", "Client", typeof(RedisStreamingProviderBuilder))]
[assembly: RegisterProvider("AzureRedisCache", "Streaming", "Client", typeof(RedisStreamingProviderBuilder))]

namespace Orleans.Hosting;

internal sealed class RedisStreamingProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(name);

        builder.AddRedisStreams(name, streamsBuilder =>
        {
            streamsBuilder.ConfigureOptions((options, services) =>
                {
                    var serviceKey = configurationSection["ServiceKey"];
                    if (!string.IsNullOrEmpty(serviceKey))
                    {
                        // 获取连接多路驱动方实例的名称 。
                        var multiplexer = services.GetRequiredKeyedService<IConnectionMultiplexer>(serviceKey);
                        options.CreateMultiplexer = _ => Task.FromResult((Multiplexer: multiplexer, IsShared: true));
                        options.ConfigurationOptions = new ConfigurationOptions();
                    }
                    else
                    {
                        // 构造连接字符串中的连接多路驱动器。
                        var connectionName = configurationSection["ConnectionName"];
                        var connectionString = configurationSection["ConnectionString"];
                        if (!string.IsNullOrEmpty(connectionName) && string.IsNullOrEmpty(connectionString))
                        {
                            var rootConfiguration = services.GetRequiredService<IConfiguration>();
                            connectionString = rootConfiguration.GetConnectionString(connectionName);
                        }

                        if (!string.IsNullOrEmpty(connectionString))
                        {
                            options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
                        }
                    }
                });

            if (configurationSection.GetValue<int?>("PartitionCount") is { } partitionCount)
            {
                streamsBuilder.ConfigurePartitioning(partitionCount);
            }
        });
    }

    public void Configure(IClientBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(name);

        builder.AddRedisStreams(name, streamsBuilder =>
        {
            streamsBuilder.ConfigureOptions((options, services) =>
            {
                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // 获取连接多路驱动方实例的名称 。
                    var multiplexer = services.GetRequiredKeyedService<IConnectionMultiplexer>(serviceKey);
                    options.CreateMultiplexer = _ => Task.FromResult((Multiplexer: multiplexer, IsShared: true));
                    options.ConfigurationOptions = new ConfigurationOptions();
                }
                else
                {
                    // 构造连接字符串中的连接多路驱动器。
                    var connectionName = configurationSection["ConnectionName"];
                    var connectionString = configurationSection["ConnectionString"];
                    if (!string.IsNullOrEmpty(connectionName) && string.IsNullOrEmpty(connectionString))
                    {
                        var rootConfiguration = services.GetRequiredService<IConfiguration>();
                        connectionString = rootConfiguration.GetConnectionString(connectionName);
                    }

                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
                    }
                }
            });

            if (configurationSection.GetValue<int?>("PartitionCount") is { } partitionCount)
            {
                streamsBuilder.ConfigurePartitioning(partitionCount);
            }
        });
    }
}

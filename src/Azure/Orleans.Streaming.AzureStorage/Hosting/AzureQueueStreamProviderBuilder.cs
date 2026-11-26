using System;
using System.Collections.Generic;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;

[assembly: RegisterProvider("AzureQueueStorage", "Streaming", "Silo", typeof(AzureQueueStreamProviderBuilder))]
[assembly: RegisterProvider("AzureQueueStorage", "Streaming", "Client", typeof(AzureQueueStreamProviderBuilder))]

namespace Orleans.Hosting;

/// <summary>
/// 配置 Azure 队列数据流提供者。
/// </summary>
public sealed class AzureQueueStreamProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    /// <summary>
    /// 配置 Azure 排队流提供方 silo.
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configurationSection">供应商配置。</param>
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.AddAzureQueueStreams(name!, GetQueueOptionBuilder(configurationSection));
    }

    /// <summary>
    /// 配置客户端上的 Azure 队列流提供者 。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流体提供者名称。</param>
    /// <param name="configurationSection">供应商配置。</param>
    public void Configure(IClientBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.AddAzureQueueStreams(name!, GetQueueOptionBuilder(configurationSection));
    }

    private static Action<OptionsBuilder<AzureQueueOptions>> GetQueueOptionBuilder(IConfigurationSection configurationSection)
    {
        return (OptionsBuilder<AzureQueueOptions> optionsBuilder) =>
        {
            optionsBuilder.Configure<IServiceProvider>((options, services) =>
            {
                var queueNames = configurationSection.GetSection("QueueNames").Get<List<string>>();
                if (queueNames != null)
                {
                    options.QueueNames = queueNames;
                }

                if (configurationSection.GetValue<TimeSpan?>(nameof(options.MessageVisibilityTimeout)) is { } visibilityTimeout)
                {
                    options.MessageVisibilityTimeout = visibilityTimeout;
                }

                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // 给客户取个名字
                    options.QueueServiceClient = services.GetRequiredKeyedService<QueueServiceClient>(serviceKey);
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
                        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
                        {
                            if (!string.IsNullOrEmpty(uri.Query))
                            {
                                // 苏苏联合
                                options.QueueServiceClient = new QueueServiceClient(uri);
                            }
                            else
                            {
                                options.QueueServiceClient = new QueueServiceClient(uri, credential: new Azure.Identity.DefaultAzureCredential());
                            }
                        }
                        else
                        {
                            options.QueueServiceClient = new QueueServiceClient(connectionString);
                        }
                    }
                }
            });
        };
    }
}

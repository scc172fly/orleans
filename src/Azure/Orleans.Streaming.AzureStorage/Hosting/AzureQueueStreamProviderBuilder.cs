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
/// 根据配置设置 Azure Queue 流提供程序。
/// </summary>
public sealed class AzureQueueStreamProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    /// <summary>
    /// 在 Silo 上配置 Azure Queue 流提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configurationSection">提供程序配置。</param>
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.AddAzureQueueStreams(name!, GetQueueOptionBuilder(configurationSection));
    }

    /// <summary>
    /// 在客户端上配置 Azure Queue 流提供程序。
    /// </summary>
    /// <param name="builder">客户端构建器。</param>
    /// <param name="name">流提供程序名称。</param>
    /// <param name="configurationSection">提供程序配置。</param>
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
                    // 按名称获取客户端。
                    options.QueueServiceClient = services.GetRequiredKeyedService<QueueServiceClient>(serviceKey);
                }
                else
                {
                    // 根据连接字符串构造连接多路复用器。（译注：此处实际创建 QueueServiceClient。）
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
                                // SAS URI
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

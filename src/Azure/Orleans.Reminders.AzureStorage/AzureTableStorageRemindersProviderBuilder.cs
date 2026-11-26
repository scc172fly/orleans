using System;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Reminders.AzureStorage;

[assembly: RegisterProvider("AzureTableStorage", "Reminders", "Silo", typeof(AzureTableStorageRemindersProviderBuilder))]

namespace Orleans.Hosting;

internal sealed class AzureTableStorageRemindersProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.UseAzureTableReminderService((OptionsBuilder<AzureTableReminderStorageOptions> optionsBuilder) =>
            optionsBuilder.Configure<IServiceProvider>((options, services) =>
            {
                var tableName = configurationSection["TableName"];
                if (!string.IsNullOrEmpty(tableName))
                {
                    options.TableName = tableName;
                }

                var serviceKey = configurationSection["ServiceKey"];
                if (!string.IsNullOrEmpty(serviceKey))
                {
                    // 给客户取个名字
                    options.TableServiceClient = services.GetRequiredKeyedService<TableServiceClient>(serviceKey);
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
                            options.TableServiceClient = new(uri);
                        }
                        else
                        {
                            options.TableServiceClient = new(connectionString);
                        }
                    }
                }
            }));
    }
}

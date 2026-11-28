using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Providers;

[assembly: RegisterProvider("AzureTableStorage", "Journaling", "Silo", typeof(AzureTableStorageJournalingProviderBuilder))]
namespace Orleans.Hosting;

internal sealed class AzureTableStorageJournalingProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        name ??= ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        builder.AddAzureTableJournalStorage(name, configure: null);
        var optionsBuilder = builder.Services.AddJournalStorageOptions<AzureTableJournalStorageOptions>(name);
        optionsBuilder.Configure<IServiceProvider>((options, services) =>
        {
            // 配置绑定生成器无法将 AzureTableJournalStorageOptions 或 Azure 客户端
            // 选项类型作为对象进行绑定：它们公开客户端实例、管道策略和委托，而且 Azure
            // 选项类型没有公共构造函数。因此改为逐个读取其可配置成员。
            var tableName = configurationSection[nameof(options.TableName)];
            if (!string.IsNullOrEmpty(tableName))
            {
                options.TableName = tableName;
            }

            options.DeleteOldGenerations = configurationSection.GetValue(nameof(options.DeleteOldGenerations), options.DeleteOldGenerations);
            options.CompactionRowCountThreshold = configurationSection.GetValue(nameof(options.CompactionRowCountThreshold), options.CompactionRowCountThreshold);
            options.CompactionSizeThreshold = configurationSection.GetValue(nameof(options.CompactionSizeThreshold), options.CompactionSizeThreshold);
            options.MaxMetadataOnlyConflictRetries = configurationSection.GetValue(nameof(options.MaxMetadataOnlyConflictRetries), options.MaxMetadataOnlyConflictRetries);
            options.MetadataOnlyConflictInitialBackoff = configurationSection.GetValue(nameof(options.MetadataOnlyConflictInitialBackoff), options.MetadataOnlyConflictInitialBackoff);
            options.MetadataOnlyConflictMaxBackoff = configurationSection.GetValue(nameof(options.MetadataOnlyConflictMaxBackoff), options.MetadataOnlyConflictMaxBackoff);

            // 通过 ConfigureTableServiceClient 创建的客户端在创建时读取 ClientOptions。
            var clientSection = configurationSection.GetSection(nameof(options.ClientOptions));
            if (clientSection.Exists())
            {
                var clientOptions = options.ClientOptions ??= new();
                clientOptions.EnableTenantDiscovery = clientSection.GetValue(nameof(clientOptions.EnableTenantDiscovery), clientOptions.EnableTenantDiscovery);
                ConfigureRetryOptions(clientSection.GetSection(nameof(clientOptions.Retry)), clientOptions.Retry);
                ConfigureDiagnosticsOptions(clientSection.GetSection(nameof(clientOptions.Diagnostics)), clientOptions.Diagnostics);
            }

            var serviceKey = configurationSection["ServiceKey"];
            if (!string.IsNullOrEmpty(serviceKey))
            {
                // 按名称获取客户端。
                options.TableServiceClient = services.GetRequiredKeyedService<TableServiceClient>(serviceKey);
            }
            else
            {
                // 根据连接字符串构造表服务客户端。
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
        });

        var journalFormatKey = configurationSection[nameof(JournaledStateManagerOptions.JournalFormatKey)];
        if (!string.IsNullOrWhiteSpace(journalFormatKey))
        {
            builder.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
        }
    }

    private static void ConfigureRetryOptions(IConfigurationSection section, RetryOptions retry)
    {
        retry.MaxRetries = section.GetValue(nameof(retry.MaxRetries), retry.MaxRetries);
        retry.Delay = section.GetValue(nameof(retry.Delay), retry.Delay);
        retry.MaxDelay = section.GetValue(nameof(retry.MaxDelay), retry.MaxDelay);
        retry.Mode = section.GetValue(nameof(retry.Mode), retry.Mode);
        retry.NetworkTimeout = section.GetValue(nameof(retry.NetworkTimeout), retry.NetworkTimeout);
    }

    private static void ConfigureDiagnosticsOptions(IConfigurationSection section, DiagnosticsOptions diagnostics)
    {
        diagnostics.IsLoggingEnabled = section.GetValue(nameof(diagnostics.IsLoggingEnabled), diagnostics.IsLoggingEnabled);
        diagnostics.IsDistributedTracingEnabled = section.GetValue(nameof(diagnostics.IsDistributedTracingEnabled), diagnostics.IsDistributedTracingEnabled);
        diagnostics.IsTelemetryEnabled = section.GetValue(nameof(diagnostics.IsTelemetryEnabled), diagnostics.IsTelemetryEnabled);
        diagnostics.IsLoggingContentEnabled = section.GetValue(nameof(diagnostics.IsLoggingContentEnabled), diagnostics.IsLoggingContentEnabled);
        diagnostics.LoggedContentSizeLimit = section.GetValue(nameof(diagnostics.LoggedContentSizeLimit), diagnostics.LoggedContentSizeLimit);
        diagnostics.ApplicationId = section[nameof(diagnostics.ApplicationId)] ?? diagnostics.ApplicationId;
        AddValues(section.GetSection(nameof(diagnostics.LoggedHeaderNames)), diagnostics.LoggedHeaderNames);
        AddValues(section.GetSection(nameof(diagnostics.LoggedQueryParameters)), diagnostics.LoggedQueryParameters);

        static void AddValues(IConfigurationSection section, IList<string> values)
        {
            foreach (var child in section.GetChildren())
            {
                if (child.Value is { } value)
                {
                    values.Add(value);
                }
            }
        }
    }
}

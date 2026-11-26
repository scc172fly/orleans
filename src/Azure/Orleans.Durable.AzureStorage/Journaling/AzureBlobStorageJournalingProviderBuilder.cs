using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Providers;

[assembly: RegisterProvider("AzureBlobStorage", "Journaling", "Silo", typeof(AzureBlobStorageJournalingProviderBuilder))]
namespace Orleans.Hosting;

internal sealed class AzureBlobStorageJournalingProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        name ??= ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        builder.AddAzureBlobJournalStorage(name, configure: null);
        var optionsBuilder = builder.Services.AddJournalStorageOptions<AzureBlobJournalStorageOptions>(name);
        optionsBuilder.Configure<IServiceProvider>((options, services) =>
        {
            var containerName = configurationSection["ContainerName"];
            if (!string.IsNullOrEmpty(containerName))
            {
                options.ContainerName = containerName;
            }

            var serviceKey = configurationSection["ServiceKey"];
            if (!string.IsNullOrEmpty(serviceKey))
            {
                // 给客户取个名字
                options.BlobServiceClient = services.GetRequiredKeyedService<BlobServiceClient>(serviceKey);
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
                        options.BlobServiceClient = new(uri);
                    }
                    else
                    {
                        options.BlobServiceClient = new(connectionString);
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
}

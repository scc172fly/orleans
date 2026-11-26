using Azure.Data.Tables;
using Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    var host = new HostBuilder()
        .UseOrleans(ConfigureSilo)
        .ConfigureLogging(logging => logging.AddConsole())
        .Build();

    await host.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static void ConfigureSilo(HostBuilderContext context, ISiloBuilder siloBuilder)
{
    siloBuilder.UseLocalhostClustering(serviceId: Constants.ServiceId, clusterId: Constants.ClusterId);

    var secrets = Secrets.TryLoadFromFile();
    if (secrets is not null)
    {
        // 使用 Azure 事件枢纽流,与 Azure 表格存储器相 PubSub 和检查站
        siloBuilder
            .AddAzureTableGrainStorage(
                "PubSubStore",
                options => options.TableServiceClient = new TableServiceClient(secrets.DataConnectionString))
            .AddEventHubStreams(Constants.StreamProvider, configurator =>
            {
                configurator.ConfigureEventHub(builder => builder.Configure(options =>
                {
                    options.ConfigureEventHubConnection(
                        secrets.EventHubConnectionString,
                        Constants.EHPath,
                        Constants.EHConsumerGroup);
                }));
                configurator.UseAzureTableCheckpointer(
                    builder => builder.Configure(options =>
                {
                    options.TableServiceClient = new TableServiceClient(secrets.DataConnectionString);
                    options.PersistInterval = TimeSpan.FromSeconds(10);
                }));
            });
        Console.WriteLine("Using Azure Event Hub streaming");
    }
    else
    {
        // 利用模拟流流促进地方发展
        siloBuilder
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams(Constants.StreamProvider);
        Console.WriteLine("Using in-memory streaming (no Secrets.json found)");
    }
}

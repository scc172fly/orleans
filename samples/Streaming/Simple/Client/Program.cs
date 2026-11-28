using GrainInterfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Orleans.Streams;
using Common;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        const int maxAttempts = 5;
        var attempt = 0;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var secrets = Secrets.TryLoadFromFile();
            var useEventHub = secrets is not null;

            var host = new HostBuilder()
                .ConfigureLogging(logging => logging.AddConsole())
                .UseOrleansClient((context, client) =>
                {
                    client
                        .UseLocalhostClustering(serviceId: Constants.ServiceId, clusterId: Constants.ClusterId)
                        .UseConnectionRetryFilter(RetryFilter);

                    if (useEventHub)
                    {
                        // 使用 Azure Event Hubs 流式传输。
                        client.AddEventHubStreams(
                            Constants.StreamProvider,
                            configurator => configurator.ConfigureEventHub(
                                builder => builder.Configure(options =>
                                {
                                    options.ConfigureEventHubConnection(
                                        secrets!.EventHubConnectionString,
                                        Constants.EHPath,
                                        Constants.EHConsumerGroup);
                                })));
                        Console.WriteLine("Using Azure Event Hub streaming");
                    }
                    else
                    {
                        // 本地开发使用内存流。
                        // 内存流由 Silo 处理，客户端只需注册提供程序。
                        client.AddMemoryStreams(Constants.StreamProvider);
                        Console.WriteLine("Using in-memory streaming (no Secrets.json found)");
                    }
                })
                .Build();

            await host.StartAsync(cts.Token);
            Console.WriteLine("Client successfully connected to silo host");
            Console.WriteLine("Press Ctrl+C to stop");

            var clusterClient = host.Services.GetRequiredService<IClusterClient>();

            // 使用已连接的客户端，要求某个 Grain 开始生成事件。
            var key = Guid.NewGuid();
            var producer = clusterClient.GetGrain<IProducerGrain>("my-producer");
            await producer.StartProducing(Constants.StreamNamespace, key);

            // 现在应当能够看到一个消费者 Grain 在 Silo 上被激活，并在收到事件时记录日志。

            // 客户端也可以订阅流。
            var streamId = StreamId.Create(Constants.StreamNamespace, key);
            var stream = clusterClient
                .GetStreamProvider(Constants.StreamProvider)
                .GetStream<int>(streamId);
            await stream.SubscribeAsync(OnNextAsync);

            // 现在客户端也会记录收到的事件。

            if (useEventHub && args.Contains("--pause-after-checkpoint", StringComparer.OrdinalIgnoreCase))
            {
                var checkpointWindow = TimeSpan.FromSeconds(20);
                Console.WriteLine(
                    $"Allowing {checkpointWindow} for the 10-second checkpoint interval before pausing stream delivery.");
                await Task.Delay(checkpointWindow, cts.Token);

                var management = clusterClient.GetGrain<IManagementGrain>(0);
                await management.SendControlCommandToProvider<PersistentStreamProvider>(
                    Constants.StreamProvider,
                    (int)PersistentStreamProviderCommand.StopAgents);

                Console.WriteLine(
                    "Stream pulling agents are paused. The producer remains active; record the next ten event numbers from the silo log, then terminate the silo.");
            }

            // 等待取消。
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 按 Ctrl+C 时出现的预期情况。
            }

            // 停止生成事件。
            Console.WriteLine("Stopping producer...");
            await producer.StopProducing();

            await host.StopAsync();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }

        static Task OnNextAsync(int item, StreamSequenceToken? token = null)
        {
            Console.WriteLine("OnNextAsync: item: {0}, token = {1}", item, token);
            return Task.CompletedTask;
        }

        async Task<bool> RetryFilter(Exception exception, CancellationToken cancellationToken)
        {
            attempt++;
            Console.WriteLine($"Cluster client attempt {attempt} of {maxAttempts} failed to connect to cluster.  Exception: {exception}");
            if (attempt > maxAttempts)
            {
                return false;
            }
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            return true;
        }
    }
}

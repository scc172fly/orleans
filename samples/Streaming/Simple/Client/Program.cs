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
                        // 使用 Azure 事件枢纽流
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
                        // 利用模拟流流促进地方发展
                        // 内存流由 silo处理,客户只需要注册的供应商
                        client.AddMemoryStreams(Constants.StreamProvider);
                        Console.WriteLine("Using in-memory streaming (no Secrets.json found)");
                    }
                })
                .Build();

            await host.StartAsync(cts.Token);
            Console.WriteLine("Client successfully connected to silo host");
            Console.WriteLine("Press Ctrl+C to stop");

            var clusterClient = host.Services.GetRequiredService<IClusterClient>();

            // 使用连接的客户端请求 grain 开始制作事件
            var key = Guid.NewGuid();
            var producer = clusterClient.GetGrain<IProducerGrain>("my-producer");
            await producer.StartProducing(Constants.StreamNamespace, key);

            // 现在,你应该看到消费者 grain 在 silo上被激活,当它接收事件时正在伐木

            // 客户端也可以订阅流
            var streamId = StreamId.Create(Constants.StreamNamespace, key);
            var stream = clusterClient
                .GetStreamProvider(Constants.StreamProvider)
                .GetStream<int>(streamId);
            await stream.SubscribeAsync(OnNextAsync);

            // 现在客户也会记录收到事件

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

            // 等待取消
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 预预计按下 Ctrl+C 时
            }

            // 停止生产
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

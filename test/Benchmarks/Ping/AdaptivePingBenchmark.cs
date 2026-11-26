using System.Net;
using BenchmarkGrainInterfaces.Ping;
using BenchmarkGrains.Ping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace Benchmarks.Ping;

/// <summary>
/// 基准线,无限期运行,利用山坡来调节货币
/// 用于寻找最佳的货币汇率水平和
/// 进行长期性能测试。
/// </summary>
public class AdaptivePingBenchmark : IDisposable
{
    public enum BenchmarkMode
    {
        /// <summary>客户在 silo 程序(低时档)</summary>内运行
        HostedClient,
        /// <summary>外部客户端连接到 silo(s)</summary>
        ExternalClient,
        /// <summary>调用从一个 silo 转到另一个(测试跨silo 性能)</summary>
        SiloToSilo
    }

    private readonly List<IHost> _hosts = new();
    private readonly IHost? _clientHost;
    private readonly IClusterClient? _client;
    private readonly BenchmarkMode _mode;
    private readonly int _numSilos;
    private readonly CancellationTokenSource _cts = new();
    private const int DefaultRequestsPerBlock = 100;
    private const int DefaultInitialStepSize = 50;
    private const int DefaultMaxStableRounds = 5;
    private const double DefaultMinimumRelativeImprovement = 0.005;
    private static readonly TimeSpan DefaultMeasurementInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMilliseconds(250);

    public string Description { get; }
    public int BestConcurrency { get; private set; }
    public double BestThroughput { get; private set; }

    public AdaptivePingBenchmark(BenchmarkMode mode = BenchmarkMode.HostedClient, int numSilos = 1)
    {
        _mode = mode;
        _numSilos = numSilos;

        // 根据模式确定配置
        bool startClient = mode == BenchmarkMode.ExternalClient;
        bool grainsOnSecondariesOnly = mode == BenchmarkMode.SiloToSilo;

        if (mode == BenchmarkMode.SiloToSilo && numSilos < 2)
        {
            numSilos = 2;
            _numSilos = 2;
        }

        Description = mode switch
        {
            BenchmarkMode.HostedClient => "Hosted Client",
            BenchmarkMode.ExternalClient when numSilos == 1 => "Client to Silo",
            BenchmarkMode.ExternalClient => $"Client to {numSilos} Silos",
            BenchmarkMode.SiloToSilo => "Silo to Silo",
            _ => mode.ToString()
        };

        // 开始 silos
        for (int i = 0; i < numSilos; i++)
        {
            var primary = i == 0 ? null : new IPEndPoint(IPAddress.Loopback, 11111);
            var hostBuilder = new HostBuilder().UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.UseLocalhostClustering(
                    siloPort: 11111 + i,
                    gatewayPort: 30000 + i,
                    primarySiloEndpoint: primary);

                siloBuilder.Configure<GrainTypeOptions>(options =>
                {
                    options.Interfaces.Add(typeof(IPingGrain));
                    options.Classes.Add(typeof(PingGrain));

                    // 用于 SiloToSilo 模式:删除 grains 从小学开始 silo 强迫交叉silo 调来电话
                    if (i == 0 && grainsOnSecondariesOnly)
                    {
                        options.Classes.Remove(typeof(PingGrain));
                    }
                });
            });

            var host = hostBuilder.Build();
            host.StartAsync().GetAwaiter().GetResult();
            _hosts.Add(host);
        }

        // 等待集集稳定在多silo 模式下
        if (numSilos > 1)
        {
            Thread.Sleep(4000);
        }

        // 必要时启动外部客户端
        if (startClient)
        {
            var hostBuilder = new HostBuilder().UseOrleansClient((ctx, clientBuilder) =>
            {
                if (numSilos == 1)
                {
                    clientBuilder.UseLocalhostClustering();
                }
                else
                {
                    var gateways = Enumerable.Range(30000, numSilos)
                        .Select(i => new IPEndPoint(IPAddress.Loopback, i))
                        .ToArray();
                    clientBuilder.UseStaticClustering(gateways);
                }
            });

            _clientHost = hostBuilder.Build();
            _clientHost.StartAsync().GetAwaiter().GetResult();
            _client = _clientHost.Services.GetRequiredService<IClusterClient>();

            // 暖和客户端连接
            var grain = _client.GetGrain<IPingGrain>(0);
            grain.Run().AsTask().GetAwaiter().GetResult();
        }

        // 连接 Ctrl+C 取消
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutdown requested...");
            _cts.Cancel();
        };
    }

    /// <summary>
    /// 根据当前模式获取 grain 工厂。
    /// </summary>
    private IGrainFactory GetGrainFactory()
    {
        return _mode == BenchmarkMode.ExternalClient
            ? _client!
            : _hosts[0].Services.GetRequiredService<IGrainFactory>();
    }

    /// <summary>
    /// 运行适应基准,通过攀山调整货币。
    /// 在 maxStableRounds 之后终止,但无统计上显著的改进(默认5),或如果0将永远持续。
    /// </summary>
    public async Task RunAsync(
        int initialConcurrency = 100,
        int minConcurrency = 1,
        int maxConcurrency = 2000,
        TimeSpan? warmupDuration = null,
        TimeSpan? measurementInterval = null,
        int maxStableRounds = DefaultMaxStableRounds,
        int initialStepSize = DefaultInitialStepSize,
        TimeSpan? sampleInterval = null,
        double minimumRelativeImprovement = DefaultMinimumRelativeImprovement)
    {
        var grainFactory = GetGrainFactory();

        Console.WriteLine($"=== Adaptive Ping Benchmark: {Description} ===");
        Console.WriteLine();

        var loadGenerator = new AdaptiveConcurrencyLoadGenerator<IPingGrain>(
            issueRequest: g => g.Run(),
            getStateForWorker: workerId => grainFactory.GetGrain<IPingGrain>(workerId),
            requestsPerBlock: DefaultRequestsPerBlock,
            warmupDuration: warmupDuration ?? TimeSpan.FromSeconds(5),
            measurementInterval: measurementInterval ?? DefaultMeasurementInterval,
            minConcurrency: minConcurrency,
            maxConcurrency: maxConcurrency,
            initialConcurrency: initialConcurrency,
            maxStableRounds: maxStableRounds,
            initialStepSize: initialStepSize,
            sampleInterval: sampleInterval ?? DefaultSampleInterval,
            minimumRelativeImprovement: minimumRelativeImprovement);

        try
        {
            await loadGenerator.RunForeverAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C 上的预期值
        }

        BestConcurrency = loadGenerator.BestConcurrency;
        BestThroughput = loadGenerator.BestThroughput;

        Console.WriteLine($"\nFinal best: {BestConcurrency} concurrency @ {BestThroughput:N0}/s");
    }

    public async Task ShutdownAsync()
    {
        if (_clientHost != null)
        {
            await _clientHost.StopAsync();
            if (_clientHost is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                _clientHost.Dispose();
        }

        _hosts.Reverse();
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            if (host is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                host.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        (_client as IDisposable)?.Dispose();
        _hosts.ForEach(h => h.Dispose());
    }

    /// <summary>
    /// 运行所有适应性活性定点基准情景,并打印摘要。
    /// </summary>
    public static async Task RunAllScenariosAsync(int maxStableRounds = DefaultMaxStableRounds)
    {
        var results = new List<(string Description, int BestConcurrency, double BestThroughput)>();

        var scenarios = new (BenchmarkMode Mode, int NumSilos)[]
        {
            (BenchmarkMode.HostedClient, 1),
            (BenchmarkMode.ExternalClient, 1),
            (BenchmarkMode.ExternalClient, 2),
            (BenchmarkMode.SiloToSilo, 2),
        };

        foreach (var (mode, numSilos) in scenarios)
        {
            var benchmark = new AdaptivePingBenchmark(mode, numSilos);
            try
            {
                await benchmark.RunAsync(maxStableRounds: maxStableRounds);
                results.Add((benchmark.Description, benchmark.BestConcurrency, benchmark.BestThroughput));
            }
            finally
            {
                await benchmark.ShutdownAsync();
                benchmark.Dispose();
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 82));
            Console.WriteLine();

            GC.Collect();
            await Task.Delay(1000); // 两种设想方案间短暂间隔
        }

        // 以 GitHub- 保存的标记表格格式打印摘要
        Console.WriteLine();
        Console.WriteLine("## Adaptive Ping Benchmark Results");
        Console.WriteLine();
        Console.WriteLine("| Scenario | Best Concurrency | Best Throughput |");
        Console.WriteLine("|----------|------------------|-----------------|");

        foreach (var (description, bestConcurrency, bestThroughput) in results)
        {
            Console.WriteLine($"| {description} | {bestConcurrency} | {bestThroughput:N0}/s |");
        }

        Console.WriteLine();
    }
}

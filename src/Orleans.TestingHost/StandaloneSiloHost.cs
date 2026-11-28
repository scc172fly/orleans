using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 独立 Silo 进程的入口点。参见 <see cref="StandaloneSiloHandle" />。
    /// </summary>
    public static class StandaloneSiloHost
    {
        /// <summary>
        /// 标准输出中位于 Silo 地址之前的前缀。
        /// </summary>
        public const string SiloAddressLog = "#### SILO ";

        /// <summary>
        /// 标准输出中位于网关地址之前的前缀。
        /// </summary>
        public const string GatewayAddressLog = "#### GATEWAY ";

        /// <summary>
        /// 指示 Silo 已启动的标准输出消息。
        /// </summary>
        public const string StartedLog = "#### STARTED";

        /// <summary>
        /// 请求 Silo 关闭的标准输入命令。
        /// </summary>
        public const string ShutdownCommand = "#### SHUTDOWN";

        /// <summary>
        /// 运行独立 Silo 进程。
        /// </summary>
        /// <param name="args">
        /// 命令行参数。第一个参数是要监控的进程的标识符，第二个参数是
        /// 序列化后的 Silo 配置。
        /// </param>
        /// <returns>Silo 关闭后完成的任务。</returns>
        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                await Console.Error.WriteLineAsync("Expected JSON-serialized configuration to be provided as an argument");
            }

            var monitorProcessId = int.Parse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            var serializedConfiguration = args[1];
            var configuration = TestClusterHostFactory.DeserializeConfiguration(serializedConfiguration);
            if (string.Equals(configuration["AttachDebugger"], "true", StringComparison.OrdinalIgnoreCase))
            {
                Debugger.Launch();
            }

            var name = configuration["SiloName"];
            using var host = TestClusterHostFactory.CreateSiloHost(name!, configuration);
            try
            {
                var cts = new CancellationTokenSource();
                using var stoppedRegistration = host.Services
                    .GetRequiredService<IHostApplicationLifetime>()
                    .ApplicationStopped.Register(cts.Cancel);
                Console.CancelKeyPress += (sender, eventArgs) => cts.Cancel();

                ListenForShutdownCommand(cts);
                MonitorParentProcess(monitorProcessId);

                await host.StartAsync(cts.Token);

                // 这是一个特殊标记行。
                var localSiloDetails = (ILocalSiloDetails)host.Services.GetService(typeof(ILocalSiloDetails))!;
                Console.WriteLine($"{SiloAddressLog}{localSiloDetails.SiloAddress.ToParsableString()}");
                Console.WriteLine($"{GatewayAddressLog}{localSiloDetails.GatewayAddress.ToParsableString()}");
#pragma warning disable CA1303 // 父进程精确匹配此机器可读的就绪标记。
                Console.WriteLine(StartedLog);
#pragma warning restore CA1303

                await cts.Token.WhenCancelled();

                await host.StopAsync(CancellationToken.None);
            }
            finally
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }

        private static void MonitorParentProcess(int monitorProcessId)
        {
            if (monitorProcessId > 0)
            {
                Console.WriteLine($"Monitoring parent process {monitorProcessId}");
                Process.GetProcessById(monitorProcessId).Exited += (o, a) =>
                {
                    Console.Error.WriteLine($"Parent process {monitorProcessId} has exited");
                    Environment.Exit(0);
                };

                _ = Task.Factory.StartNew(async _ =>
                {
                    try
                    {
                        while (true)
                        {
                            await Task.Delay(5000);
                            if (!Array.Exists(Process.GetProcesses(), p => p.Id == monitorProcessId))
                            {
                                await Console.Error.WriteLineAsync($"Parent process {monitorProcessId} has exited");
                                Environment.Exit(0);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略所有错误。
                    }
                },
                null,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            }
        }

        private static void ListenForShutdownCommand(CancellationTokenSource cts)
        {
            // 启动线程以监控来自标准输入的关闭命令。
            _ = Task.Factory.StartNew(_ =>
            {
                try
                {
                    while (true)
                    {
                        var text = Console.ReadLine();
                        if (string.Equals(text, ShutdownCommand, StringComparison.Ordinal))
                        {
                            Console.WriteLine("Shutdown requested");
                            cts.Cancel();
                            return;
                        }
                    }
                }
                catch
                {
                    // 忽略所有错误。
                }
            },
            null,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        }

        private static Task WhenCancelled(this CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var waitForCancellation = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(obj =>
            {
                var tcs = (TaskCompletionSource<object?>)obj!;
                tcs.TrySetResult(null);
            }, waitForCancellation);

            return waitForCancellation.Task;
        }
    }
}

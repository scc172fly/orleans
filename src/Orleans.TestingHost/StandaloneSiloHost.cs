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
    /// 独立 silo 进程切入点。见 <see cref="StandaloneSiloHandle" />。
    /// </summary>
    public static class StandaloneSiloHost
    {
        /// <summary>
        /// 在 silo 地址前的标准输出前缀。
        /// </summary>
        public const string SiloAddressLog = "#### SILO ";

        /// <summary>
        /// 网关地址前的标准输出前缀。
        /// </summary>
        public const string GatewayAddressLog = "#### GATEWAY ";

        /// <summary>
        /// 标准输出信息,其中显示 silo 已经开始。
        /// </summary>
        public const string StartedLog = "#### STARTED";

        /// <summary>
        /// 要求关闭 silo 的标准输入命令。
        /// </summary>
        public const string ShutdownCommand = "#### SHUTDOWN";

        /// <summary>
        /// 运行独立 silo 进程。
        /// </summary>
        /// <param name="args">
        /// 命令行参数。 第一个参数是监测程序的标识符,第二个参数是
        /// 序列化的 silo 配置 。
        /// </param>
        /// <returns>一项任务,当它完成的时候, silo 关闭了</returns>
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

                // 这是特别的标记线
                var localSiloDetails = (ILocalSiloDetails)host.Services.GetService(typeof(ILocalSiloDetails))!;
                Console.WriteLine($"{SiloAddressLog}{localSiloDetails.SiloAddress.ToParsableString()}");
                Console.WriteLine($"{GatewayAddressLog}{localSiloDetails.GatewayAddress.ToParsableString()}");
#pragma warning disable CA1303 // The parent process matches this machine-readable readiness marker exactly.
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
                        // 忽略所有错误 。
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
            // 从标准输入开始显示关闭命令的线条监测 。
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
                    // 忽略所有错误 。
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

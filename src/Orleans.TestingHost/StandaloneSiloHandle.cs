using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 为每个 Silo 启动独立进程的 Silo 句柄及工厂。
    /// </summary>
    public class StandaloneSiloHandle : SiloHandle
    {
        private readonly StringBuilder _outputBuilder;
        private readonly TaskCompletionSource<bool> _startedEvent;
        private readonly TaskCompletionSource<bool> _outputCloseEvent;
        private readonly StringBuilder _errorBuilder;
        private readonly TaskCompletionSource<bool> _errorCloseEvent;
        private readonly EventHandler _processExitHandler;
        private bool isActive = true;
        private Task _runTask = null!;

        /// <summary>
        /// 用于标识要启动进程的配置键。
        /// </summary>
        public const string ExecutablePathConfigKey = "ExecutablePath";

        /// <summary>获取 Silo 宿主的引用。</summary>
        private Process Process { get; set; }

        /// <inheritdoc />
        public override bool IsActive => isActive;

        /// <summary>
        /// 初始化 <see cref="StandaloneSiloHandle"/> 类的新实例。
        /// </summary>
        /// <param name="siloName">Silo 名称。</param>
        /// <param name="configuration">要传递给独立 Silo 进程的配置。</param>
        /// <param name="executablePath">承载 Silo 的可执行文件路径。</param>
        public StandaloneSiloHandle(string siloName, IConfiguration configuration, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new ArgumentException("Must provide at least one assembly path");
            }

            Name = siloName;

            // 如果调试器已附加到此进程，则给它附加到远程进程的机会。
            if (Debugger.IsAttached)
            {
                configuration["AttachDebugger"] = "true";
            }

            var serializedConfiguration = TestClusterHostFactory.SerializeConfiguration(configuration);

            Process = new Process();
            Process.StartInfo = new ProcessStartInfo(executablePath)
            {
                ArgumentList = { Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture), serializedConfiguration },
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                WorkingDirectory = new FileInfo(executablePath).Directory!.FullName,
                UseShellExecute = false,
            };

            _outputBuilder = new StringBuilder();
            _outputCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _startedEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    _outputCloseEvent.SetResult(true);
                }
                else
                {
                    // 读取进程的标准输出以获取状态更新。
                    if (!_startedEvent.Task.IsCompleted)
                    {
                        if (e.Data.StartsWith(StandaloneSiloHost.SiloAddressLog, StringComparison.Ordinal))
                        {
                            SiloAddress = Orleans.Runtime.SiloAddress.FromParsableString(e.Data[StandaloneSiloHost.SiloAddressLog.Length..]);
                        }
                        else if (e.Data.StartsWith(StandaloneSiloHost.GatewayAddressLog, StringComparison.Ordinal))
                        {
                            GatewayAddress = Orleans.Runtime.SiloAddress.FromParsableString(e.Data[StandaloneSiloHost.GatewayAddressLog.Length..]);
                        }
                        else if (e.Data.StartsWith(StandaloneSiloHost.StartedLog, StringComparison.Ordinal))
                        {
                            _startedEvent.TrySetResult(true);
                        }
                    }

                    lock (_outputBuilder)
                    {
                        _outputBuilder.AppendLine(e.Data);
                    }
                }
            };

            _errorBuilder = new StringBuilder();
            _errorCloseEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    _errorCloseEvent.SetResult(true);
                }
                else
                {
                    lock (_errorBuilder)
                    {
                        _errorBuilder.AppendLine(e.Data);
                    }
                }
            };

            var selfReference = new WeakReference<StandaloneSiloHandle>(this);
            _processExitHandler = (o, e) =>
            {
                if (selfReference.TryGetTarget(out var target))
                {
                    try
                    {
                        target.Process.Kill(entireProcessTree: true);
                    }
                    catch
                    {

                    }
                }
            };
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }

        /// <summary>
        /// 启动新进程以承载 Silo，使用配置的 "ExecutablePath" 属性中提供的可执行文件作为入口点。
        /// </summary>
        public static async Task<SiloHandle> Create(
            string siloName,
            IConfiguration configuration)
        {
            var executablePath = configuration[ExecutablePathConfigKey];
            var result = new StandaloneSiloHandle(siloName, configuration, executablePath!);
            await result.StartAsync();
            return result;
        }

        /// <summary>
        /// 创建启动新进程中的 Silo 的委托，使用提供的可执行文件作为该 Silo 的入口点。
        /// </summary>
        /// <param name="executablePath">启动的 Silo 的入口点。</param>
        public static Func<string, IConfiguration, Task<SiloHandle>> CreateDelegate(string executablePath)
        {
            return async (siloName, configuration) =>
            {
                var result = new StandaloneSiloHandle(siloName, configuration, executablePath);
                await result.StartAsync();
                return result;
            };
        }

        /// <summary>
        /// 创建启动新进程中的 Silo 的委托，使用提供的程序集（如果是库，则使用对应的可执行程序集）作为该 Silo 的入口点。
        /// </summary>
        /// <param name="assembly">启动的 Silo 的入口点。如果提供的程序集是库（dll），则改为调用其同级可执行程序集。</param>
        public static Func<string, IConfiguration, Task<SiloHandle>> CreateForAssembly(Assembly assembly)
        {
            var executablePath = GetExecutablePath(
                assembly,
                GetAssemblyLocation(assembly),
                ReferenceEquals(assembly, Assembly.GetEntryAssembly()),
                Environment.ProcessPath);
            return CreateDelegate(executablePath);
        }

        internal static string GetExecutablePath(Assembly assembly, string assemblyLocation, bool isEntryAssembly, string? processPath)
        {
            var executablePath = string.IsNullOrEmpty(assemblyLocation) && isEntryAssembly ? processPath : assemblyLocation;
            if (string.IsNullOrEmpty(executablePath))
            {
                throw new FileNotFoundException($"Cannot find assembly location for assembly {assembly}");
            }

            var originalFileInfo = new FileInfo(executablePath);
            if (!originalFileInfo.Exists)
            {
                throw new FileNotFoundException($"Cannot find assembly location for assembly {assembly}. Location property returned \"{executablePath}\"");
            }

            FileInfo target;
            if (string.Equals(".dll", originalFileInfo.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    target = new FileInfo(Path.GetFileNameWithoutExtension(originalFileInfo.FullName) + ".exe");
                }
                else
                {
                    // 在类 Unix 操作系统中，可执行文件通常没有扩展名。
                    target = new FileInfo(Path.GetFileNameWithoutExtension(originalFileInfo.FullName));
                }
            }
            else
            {
                target = originalFileInfo;
            }

            if (!target.Exists)
            {
                throw new FileNotFoundException($"Target assembly \"{target.FullName}\" does not exist");
            }

            return target.FullName;
        }

        [UnconditionalSuppressMessage(
            "SingleFile",
            "IL3000:Avoid accessing Assembly file path when publishing as a single file",
            Justification = "CreateForAssembly handles the empty location of a bundled entry assembly using the current process path.")]
        private static string GetAssemblyLocation(Assembly assembly) => assembly.Location;

        private async Task StartAsync()
        {
            try
            {
                if (!Process.Start())
                {
                    throw new InvalidOperationException("No process was started");
                }
            }
            catch (Exception)
            {
                isActive = false;
                throw;
            }

            _runTask = Task.Run(async () =>
            {
                try
                {
                    Process.BeginOutputReadLine();
                    Process.BeginErrorReadLine();

                    var waitForExit = Task.Factory.StartNew(
                        process => ((Process)process!).WaitForExit(-1),
                        Process,
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning | TaskCreationOptions.RunContinuationsAsynchronously,
                        TaskScheduler.Default);
                    await Task.WhenAll(waitForExit, _outputCloseEvent.Task, _errorCloseEvent.Task);

                    if (!await waitForExit)
                    {
                        try
                        {
                            Process.Kill();
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception exception)
                {
                    _startedEvent.TrySetException(exception);
                }
            });

            var task = await Task.WhenAny(_startedEvent.Task, _outputCloseEvent.Task, _errorCloseEvent.Task);
            if (!ReferenceEquals(task, _startedEvent.Task))
            {
                string output;
                lock (_outputBuilder)
                {
                    output = _outputBuilder.ToString();
                }

                string error;
                lock (_errorBuilder)
                {
                    error = _errorBuilder.ToString();
                }

                throw new Exception($"Process failed to start correctly.\nOutput:\n{output}\nError:\n{error}");
            }
        }

        /// <inheritdoc />
        public override async Task StopSiloAsync(bool stopGracefully)
        {
            using var cancellation = new CancellationTokenSource();
            var ct = cancellation.Token;

            if (!stopGracefully) cancellation.Cancel();

            await StopSiloAsync(ct);
        }

        /// <inheritdoc />
        public override async Task StopSiloAsync(CancellationToken ct)
        {
            if (!IsActive) return;

            try
            {
                if (ct.IsCancellationRequested)
                {
                    this.Process?.Kill();
                }
                else
                {
                    using var registration = ct.Register(() =>
                    {
                        var process = this.Process;
                        if (process is not null)
                        {
                            process.Kill();
                        }
                    });

                    await this.Process.StandardInput.WriteLineAsync(StandaloneSiloHost.ShutdownCommand);
                    using var linkedCts = new CancellationTokenSource();
                    await Task.WhenAny(_runTask, Task.Delay(TimeSpan.FromMinutes(2), linkedCts.Token));
                }
            }
            finally
            {
                this.isActive = false;
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!this.IsActive) return;

            if (disposing)
            {
                try
                {
                    StopSiloAsync(true).GetAwaiter().GetResult();
                }
                catch
                {
                }

                this.Process?.Dispose();
            }

            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (!this.IsActive) return;

            await StopSiloAsync(true).ConfigureAwait(false);
            this.Process?.Dispose();
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        }
    }
}

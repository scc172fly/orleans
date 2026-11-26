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
    /// 一种 silo 处理和工厂,为每个 silo产生一个单独的过程。
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
        /// 用于识别启动进程配置密钥 。
        /// </summary>
        public const string ExecutablePathConfigKey = "ExecutablePath";

        /// <summary>获取 silo 主机的引用。</summary>
        private Process Process { get; set; }

        /// <inheritdoc />
        public override bool IsActive => isActive;

        /// <summary>
        /// 初始化 <see cref="StandaloneSiloHandle"/> 类的新实例。
        /// </summary>
        /// <param name="siloName"> silo的名称。</param>
        /// <param name="configuration">将传递到独立 silo 进程的配置。</param>
        /// <param name="executablePath">主机 silo的可执行文件路径。</param>
        public StandaloneSiloHandle(string siloName, IConfiguration configuration, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new ArgumentException("Must provide at least one assembly path");
            }

            Name = siloName;

            // 如果调试器与此进程有关,则给它一个机会附加远程进程。
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
                    // 读取状态更新过程中的标准输出 。
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
        /// 启动新的程序以主机 silo,使用配置中提供的可执行文件“ExecutablePath" 财产作为入境点。
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
        /// 创建代表,产生 silo 在新程序中,使用所提供的可执行文件作为该程序的切入点 silo.
        /// </summary>
        /// <param name="executablePath">产卵的切入点 silos.</param>
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
        /// 创建一个代表,在新的过程中产生 silo ,该代表的切入点是提供组装(或可执行对应文件,如果它是图书馆的话)作为该 silo的切入点。
        /// </summary>
        /// <param name="assembly">产卵的切入点 silos。如果提供的组装是一个库(dll),则将援引其可执行的硅状组装。</param>
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
                    // 在类似unix的操作系统上,可执行文件一般没有扩展名。
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

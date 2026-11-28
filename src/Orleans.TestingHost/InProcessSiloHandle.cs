using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 表示在同一进程和 AppDomain 中部署的 Silo 的句柄。
    /// </summary>
    public class InProcessSiloHandle : SiloHandle
    {
        private bool isActive = true;
        private int disposed;

        /// <summary>获取 Silo 宿主的引用。</summary>
        public IHost SiloHost { get; init; } = null!;

        /// <summary>
        /// 获取 Silo 的服务提供程序。
        /// </summary>
        public IServiceProvider ServiceProvider => SiloHost.Services;

        /// <inheritdoc />
        public override bool IsActive => isActive;

        /// <summary>
        /// 创建 Silo 句柄。
        /// </summary>
        /// <param name="siloName">Silo 名称。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">在宿主构建器执行构建之前调用的可选委托。</param>
        /// <returns>Silo 句柄。</returns>
        public static async Task<InProcessSiloHandle> CreateAsync(
            string siloName,
            IConfiguration configuration,
            Action<IHostBuilder>? postConfigureHostBuilder = null)
            => await CreateAsync(siloName, configuration, postConfigureHostBuilder, CancellationToken.None);

        /// <summary>
        /// 创建 Silo 句柄。
        /// </summary>
        /// <param name="siloName">Silo 名称。</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">在宿主构建器执行构建之前调用的可选委托。</param>
        /// <param name="cancellationToken">用于取消 Silo 启动的令牌。</param>
        /// <returns>Silo 句柄。</returns>
        public static async Task<InProcessSiloHandle> CreateAsync(
            string siloName,
            IConfiguration configuration,
            Action<IHostBuilder>? postConfigureHostBuilder,
            CancellationToken cancellationToken)
        {
            var host = await Task.Run(async () =>
            {
                var result = TestClusterHostFactory.CreateSiloHost(siloName, configuration, postConfigureHostBuilder);
                try
                {
                    await result.StartAsync(cancellationToken);
                    return result;
                }
                catch
                {
                    if (result is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else
                    {
                        result.Dispose();
                    }

                    throw;
                }
            }, cancellationToken);

            var retValue = new InProcessSiloHandle
            {
                Name = siloName,
                SiloHost = host,
                SiloAddress = host.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress,
                GatewayAddress = host.Services.GetRequiredService<ILocalSiloDetails>().GatewayAddress,
            };

            return retValue;
        }

        /// <inheritdoc />
        public override async Task StopSiloAsync(bool stopGracefully)
        {
            using var cancellation = new CancellationTokenSource();
            var ct = cancellation.Token;

            if (!stopGracefully)
                cancellation.Cancel();

            await StopSiloAsync(ct);
        }

        /// <inheritdoc />
        public override async Task StopSiloAsync(CancellationToken ct)
        {
            if (!IsActive) return;

            try
            {
                await Task.Run(() => this.SiloHost.StopAsync(ct), CancellationToken.None);
            }
            catch (Exception exc)
            {
                WriteLog(exc);
                throw;
            }
            finally
            {
                this.isActive = false;
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            if (this.IsActive)
            {
                try
                {
                    StopSiloAsync(true).GetAwaiter().GetResult();
                }
                catch
                {
                }
            }

            this.SiloHost?.Dispose();
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (this.IsActive)
                {
                    await StopSiloAsync(true).ConfigureAwait(false);
                }
            }
            finally
            {
                if (this.SiloHost is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    this.SiloHost?.Dispose();
                }
            }
        }

        private static void WriteLog(object value)
        {
            Console.WriteLine(value?.ToString());
        }
    }
}

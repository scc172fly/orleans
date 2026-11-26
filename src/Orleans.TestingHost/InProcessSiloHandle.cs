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
    /// 表示手柄为 silo 在同一进程中部署的 AppDomain.
    /// </summary>
    public class InProcessSiloHandle : SiloHandle
    {
        private bool isActive = true;
        private int disposed;

        /// <summary>获取 silo 主机的引用。</summary>
        public IHost SiloHost { get; init; } = null!;

        /// <summary>
        /// 获取 silo的服务提供商。
        /// </summary>
        public IServiceProvider ServiceProvider => SiloHost.Services;

        /// <inheritdoc />
        public override bool IsActive => isActive;

        /// <summary>
        /// 创建 silo 控件。
        /// </summary>
        /// <param name="siloName">国名 国 名 国 名 silo.</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">一位在建造主机建筑商之前被援引的可选代表。</param>
        /// <returns> silo 控件。</returns>
        public static async Task<InProcessSiloHandle> CreateAsync(
            string siloName,
            IConfiguration configuration,
            Action<IHostBuilder>? postConfigureHostBuilder = null)
            => await CreateAsync(siloName, configuration, postConfigureHostBuilder, CancellationToken.None);

        /// <summary>
        /// 创建 silo 控件。
        /// </summary>
        /// <param name="siloName">国名 国 名 国 名 silo.</param>
        /// <param name="configuration">配置。</param>
        /// <param name="postConfigureHostBuilder">一位在建造主机建筑商之前被援引的可选代表。</param>
        /// <param name="cancellationToken">取消时使用的牌点 silo 启动。</param>
        /// <returns> silo 控件。</returns>
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 表示远程部署的 Silo 的句柄。
    /// </summary>
    public abstract class SiloHandle : IDisposable, IAsyncDisposable
    {
        /// <summary>获取或设置集群配置。</summary>
        public TestClusterOptions? ClusterOptions { get; set; }

        /// <summary>获取或设置集群中的实例编号。</summary>
        public short InstanceNumber { get; set; }

        /// <summary>获取或设置 Silo 名称。</summary>
        public string Name { get; set; } = null!;

        /// <summary>获取或设置 Silo 地址。</summary>
        public SiloAddress SiloAddress { get; set; } = null!;

        /// <summary>获取或设置 Silo 的网关地址。</summary>
        public SiloAddress GatewayAddress { get; set; } = null!;

        /// <summary>获取远程 Silo 是否预期处于活动状态。</summary>
        public abstract bool IsActive { get; }

        /// <summary>停止远程 Silo。</summary>
        /// <param name="stopGracefully">指定应正常停止还是突然终止 Silo。</param>
        public abstract Task StopSiloAsync(bool stopGracefully);

        /// <summary>停止远程 Silo。</summary>
        /// <param name="stopGracefully">指定应正常停止还是突然终止 Silo。</param>
        /// <param name="cancellationToken">
        /// 指定用于正常关闭或取消等待突然终止完成的取消令牌。
        /// </param>
        public virtual Task StopSiloAsync(bool stopGracefully, CancellationToken cancellationToken)
            => stopGracefully
                ? StopSiloAsync(cancellationToken)
                : StopSiloAsync(stopGracefully).WaitAsync(cancellationToken);

        /// <summary>停止远程 Silo。此方法不能与 AppDomain 一起使用。</summary>
        /// <param name="ct">指定关闭序列使用的取消令牌。</param>
        public abstract Task StopSiloAsync(CancellationToken ct);

        /// <summary>执行应用程序定义的释放、解除或重置非托管资源的任务。</summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>执行应用程序定义的释放、解除或重置非托管资源的任务。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsActive) return;

            // 不要尝试在终结器线程中执行开销较高的阻塞操作。
            // 具体的 SiloHandle 实现可以拥有自己的清理功能。
            if (disposing)
            {
                StopSiloAsync(true).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc />
        public abstract ValueTask DisposeAsync();

        /// <inheritdoc />
        ~SiloHandle()
        {
            Dispose(false);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return SiloAddress.ToString();
        }
    }
}

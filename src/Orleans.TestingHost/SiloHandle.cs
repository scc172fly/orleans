using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 表示遥控部署的 silo 的控管
    /// </summary>
    public abstract class SiloHandle : IDisposable, IAsyncDisposable
    {
        /// <summary> 获取或设置组群配置 </summary>
        public TestClusterOptions? ClusterOptions { get; set; }

        /// <summary> 获取或设置组内的实例编号。</summary>
        public short InstanceNumber { get; set; }

        /// <summary> 获取或设置 silo </summary>的名称
        public string Name { get; set; } = null!;

        /// <summary>获取或设置 silo</summary>地址
        public SiloAddress SiloAddress { get; set; } = null!;

        /// <summary>获取或设置 silo.</summary>
        public SiloAddress GatewayAddress { get; set; } = null!;

        /// <summary>获取远程 silo 是否预期活动</summary>
        public abstract bool IsActive { get; }

        /// <summary>停止远程 silo</summary>
        /// <param name="stopGracefully">指定是否应该优雅地或突然地停止 silo 。</param>
        public abstract Task StopSiloAsync(bool stopGracefully);

        /// <summary>停止远程 silo。</summary>
        /// <param name="stopGracefully">指定是否应该优雅地或突然地停止 silo 。</param>
        /// <param name="cancellationToken">
        /// 指定优雅关闭或取消突然关闭的等待的取消符。
        /// </param>
        public virtual Task StopSiloAsync(bool stopGracefully, CancellationToken cancellationToken)
            => stopGracefully
                ? StopSiloAsync(cancellationToken)
                : StopSiloAsync(stopGracefully).WaitAsync(cancellationToken);

        /// <summary>停止远程 silo。该方法不能用于 AppDomain</summary>
        /// <param name="ct">指定用于关闭序列的取消符</param>
        public abstract Task StopSiloAsync(CancellationToken ct);

        /// <summary>执行与释放、释放或重新设置未管理的资源有关的应用程序定义任务。</summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>执行与释放、释放或重新设置未管理的资源有关的应用程序定义任务。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsActive) return;

            // 不要试图在最后的线索中执行昂贵的封堵操作 。
            // 具体 SiloHandle 实施可以有其自己的清理功能
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

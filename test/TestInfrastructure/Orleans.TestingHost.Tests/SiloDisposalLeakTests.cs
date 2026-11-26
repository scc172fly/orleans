using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Placement;
using Orleans.TestingHost.Tests.Grains;
using TestExtensions;
using Xunit;

namespace Orleans.TestingHost.Tests
{
    /// <summary>
    /// https://github.com/dotnet/orleans/issues/10258的回归测试。
    ///
    /// 正在停止一个进程 silo (e.g. 通过 <see cref="InProcessTestCluster.StopAllSilosAsync"/>)
    /// 在处置集束之前,必须仍然处理基本 <c>SiloHost</c>。
    /// 主机的 <see cref="IServiceProvider"/> 及其背景线(如“监视者”)是
    /// 从未释放,在整个过程的寿命期间泄漏整个 silo 。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestCategory("Functional")]
    public class SiloDisposalLeakTests
    {
        [Fact]
        public async Task StoppedSilo_AfterClusterDispose_HostIsDisposed()
        {
            var builder = new InProcessTestClusterBuilder(1);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var serviceProvider = cluster.Silos[0].ServiceProvider;

            // 检查是否合乎情理:在 silo 运行期间,服务供应商可用。
            Assert.NotNull(serviceProvider.GetService<ILocalSiloDetails>());

            // 问题的模式:先停止 silos ,然后处理组群。
            await cluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
            await cluster.DisposeAsync();

            // 如果处置了东道主,其服务提供商也将处置并解决抛掷问题。
            // 在修复之前,当 silo 已经停止处理时,主机从未处置过,因此
            // 该决议将取得成功, silo (包括其 " 观察线 " )将保留。
            Assert.Throws<ObjectDisposedException>(() => serviceProvider.GetService<ILocalSiloDetails>());
        }

        [Fact]
        public async Task StoppedSilo_AfterHandleDispose_HostIsDisposed()
        {
            var builder = new InProcessTestClusterBuilder(1);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var handle = cluster.Silos[0];
            var serviceProvider = handle.ServiceProvider;

            // 停止 silo,然后直接处置处理。即使处置也必须将主机也处理
            // 虽然把手不再活动
            await handle.StopSiloAsync(
                stopGracefully: true,
                TestContext.Current.CancellationToken);
            await handle.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => serviceProvider.GetService<ILocalSiloDetails>());

            await cluster.DisposeAsync();
        }

        [Fact]
        public void StoppedSilo_AfterClusterDispose_ServiceProviderIsCollected()
        {
            // 部署、停止和处置完全在一个非内嵌同步助手中进行,该助手返回
            // 仅 a WeakReference。这使集集和每个中间局域人无法参与其中
            // 框架,以便一旦助手返回,没有任何根 silo服务供应商的服务供应商,除非
            // 运行中的主机及其背景线索( 在修正前) 。
            var weakRef = DeployStopAndDispose();

            AssertCollected(weakRef);
        }

        [Fact]
        public void KilledSilo_AfterClusterDispose_ServiceProviderIsCollected()
        {
            var weakRef = DeployKillAndDispose();

            AssertCollected(weakRef);
        }

        [Fact]
        public async Task KilledSilo_WithInFlightOutboundCall_DisposesPromptly()
        {
            const int PendingCallCount = 8;

            // CI中出现的僵局的倒退试验:杀死其无国籍者 silo
            // 工人正在等待外出电话(他们的答复一旦收到,就永远无法到达)。 silo 停止)
            // 处置时不能挂挂 silo 无国籍人的移徙工人 grain 内下处置
            // 工人的电话被打坏,工人恢复了工作,他们重新尝试
            // 并在关闭后登记新的回调,除非登记工作结束。
            var builder = new InProcessTestClusterBuilder(2);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            await using var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var client = cluster.Client;
            var launcherHandle = cluster.Silos[0];
            var remoteHandle = cluster.Silos[1];

            // 将发射装置放在将要被杀死的 silo 上。
            // 放在该 silo上。
            RequestContext.Set(IPlacementDirector.PlacementHintKey, launcherHandle.SiloAddress);
            var launcher = client.GetGrain<ILauncherGrain>(Guid.NewGuid());
            try
            {
                Assert.Equal(launcherHandle.SiloAddress.ToString(), await launcher.GetSiloIdentity());
            }
            finally
            {
                RequestContext.Remove(IPlacementDirector.PlacementHintKey);
            }

            var remoteKeys = new List<Guid>(PendingCallCount);
            for (var call = 0; call < PendingCallCount; call++)
            {
                // 将每个阻塞器都放在幸存的上 silo 工作者的召唤,确是外出的真正喊叫,
                // 当发射装置 silo 被杀死时,其答复被切断。
                var remoteKey = Guid.NewGuid();
                RequestContext.Set(IPlacementDirector.PlacementHintKey, remoteHandle.SiloAddress);
                var remote = client.GetGrain<IRemoteBlockerGrain>(remoteKey);
                try
                {
                    Assert.Equal(remoteHandle.SiloAddress.ToString(), await remote.GetSiloIdentity());
                }
                finally
                {
                    RequestContext.Remove(IPlacementDirector.PlacementHintKey);
                }

                remoteKeys.Add(remoteKey);

                // 触发来自一个无国籍工人的外出电话 与发射器同地办公 silo.
                var worker = client.GetGrain<ILocalWorkerGrain>(Guid.NewGuid());
                await launcher.StartBlockingCall(worker, remote);
                Assert.True(
                    await RemoteBlockerGrain.WaitForEntered(
                        remoteKey,
                        TimeSpan.FromSeconds(30),
                        TestContext.Current.CancellationToken),
                    "The blocking call was never entered.");
            }

            // 杀死发射装置的 silo (可读) 并处置其主机。 在修好前, 此程序可以挂起
            // 如果取消会中断回调时针关闭, 则在回调发生错误之前终止。
            var killAndDispose = cluster.KillSiloAsync(
                launcherHandle,
                TestContext.Current.CancellationToken);
            var completed = await Task.WhenAny(killAndDispose, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            Assert.True(completed == killAndDispose, "Killing a silo with an in-flight outbound call should not hang host disposal.");
            await killAndDispose;

            // 允许生存 silo 彻底关闭
            foreach (var remoteKey in remoteKeys)
            {
                RemoteBlockerGrain.Release(remoteKey);
            }
        }

        private static void AssertCollected(WeakReference weakRef)
        {
            // 背景线(e.g. 监控狗)可以简短地让提供者根植于他们的堆叠
            // 并且他们逐渐下降,所以他们要以一个固定的暂停期重试,不要只坚持一个GC。
            for (var attempt = 0; attempt < 20 && weakRef.IsAlive; attempt++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                if (weakRef.IsAlive)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }

            Assert.False(weakRef.IsAlive, "The stopped silo's service provider should be collectible after the cluster is disposed.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DeployStopAndDispose()
        {
            var cluster = BuildCluster();
            cluster.DeployAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            var weakRef = new WeakReference(cluster.Silos[0].ServiceProvider);
            cluster.StopAllSilosAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            cluster.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return weakRef;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DeployKillAndDispose()
        {
            var cluster = BuildCluster();
            cluster.DeployAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            var weakRef = new WeakReference(cluster.Silos[0].ServiceProvider);
            cluster.KillSiloAsync(cluster.Silos[0], TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            cluster.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return weakRef;
        }

        private static InProcessTestCluster BuildCluster()
        {
            var builder = new InProcessTestClusterBuilder(1);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            return builder.Build();
        }
    }
}

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
    /// 针对 https://github.com/dotnet/orleans/issues/10258 的回归测试。
    ///
    /// 在释放集群之前停止进程内 Silo（例如通过 <see cref="InProcessTestCluster.StopAllSilosAsync"/>）时，
    /// 仍必须释放底层 <c>SiloHost</c>。否则，
    /// 宿主的 <see cref="IServiceProvider"/> 及其后台线程（如 Watchdog）
    /// 将永远无法释放，导致整个 Silo 在进程生命周期内泄漏。
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

            // 基本检查：Silo 运行期间服务提供程序可用。
            Assert.NotNull(serviceProvider.GetService<ILocalSiloDetails>());

            // 问题报告中的操作模式：先停止 Silo，再释放集群。
            await cluster.StopAllSilosAsync(TestContext.Current.CancellationToken);
            await cluster.DisposeAsync();

            // 若宿主已释放，其服务提供程序也已释放，此时解析服务会引发异常。
            // 修复之前，若 Silo 已停止，则宿主永远不会被释放，因此
            // 此次解析会成功，并且 Silo（包括其 Watchdog 线程）会一直保留。
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

            // 停止 Silo，再直接释放句柄。即使句柄已不再处于活动状态，
            // 释放操作也必须释放宿主。
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
            // 在一个禁止内联的同步辅助方法内完成部署、停止和释放，该方法仅返回
            // WeakReference。这使集群及所有中间局部变量都不位于当前
            // 栈帧中；因此辅助方法返回后，不再有对象使 Silo 的服务提供程序保持可达，
            // 但修复之前仍在运行的宿主及其后台线程除外。
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

            // 针对 CI 中出现的死锁的回归测试：终止一个 Silo 时，如果其无状态
            // 工作者正在等待出站调用（Silo 停止后永远无法收到响应），
            // 释放 Silo 宿主的过程不得挂起。无状态工作者的 Grain 上下文释放过程
            // 会等待每个工作者停用。使这些调用失败可恢复工作者的执行；它们会重试，
            // 若未关闭回调注册，则会在关闭清理后注册新回调。
            var builder = new InProcessTestClusterBuilder(2);
            builder.ConfigureHost(hostBuilder => TestDefaultConfiguration.ConfigureHostConfiguration(hostBuilder.Configuration));
            await using var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            var client = cluster.Client;
            var launcherHandle = cluster.Silos[0];
            var remoteHandle = cluster.Silos[1];

            // 将启动器放置在将被终止的 Silo 上。它调用的无状态工作者
            // 也放置在同一个 Silo 上。
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
                // 将每个阻塞者放置在仍存活的 Silo 上，使工作者的调用成为真正的出站
                // 请求；启动器所在 Silo 被终止时，其响应路径将被切断。
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

                // 从与启动器位于同一 Silo 的无状态工作者发起出站调用。
                var worker = client.GetGrain<ILocalWorkerGrain>(Guid.NewGuid());
                await launcher.StartBlockingCall(worker, remote);
                Assert.True(
                    await RemoteBlockerGrain.WaitForEntered(
                        remoteKey,
                        TimeSpan.FromSeconds(30),
                        TestContext.Current.CancellationToken),
                    "The blocking call was never entered.");
            }

            // 强制终止启动器所在的 Silo（非正常关闭）并释放其宿主。修复之前，如果
            // 取消操作在回调被置为失败之前打断了回调计时器的关闭，此处可能挂起。
            var killAndDispose = cluster.KillSiloAsync(
                launcherHandle,
                TestContext.Current.CancellationToken);
            var completed = await Task.WhenAny(killAndDispose, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            Assert.True(completed == killAndDispose, "Killing a silo with an in-flight outbound call should not hang host disposal.");
            await killAndDispose;

            // 允许仍存活的 Silo 正常关闭。
            foreach (var remoteKey in remoteKeys)
            {
                RemoteBlockerGrain.Release(remoteKey);
            }
        }

        private static void AssertCollected(WeakReference weakRef)
        {
            // 后台线程（如 Watchdog）在逐步退出时，其栈可能短暂地使服务提供程序保持可达，
            // 因此在有限超时内重试，而不是在一次 GC 后立即断言。
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

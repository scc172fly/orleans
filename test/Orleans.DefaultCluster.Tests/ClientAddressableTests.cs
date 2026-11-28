using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 测试 Orleans 中可寻址的客户端对象，它们允许 Grain 回调客户端对象。
    /// 此功能实现双向通信，Grain 可以调用托管在客户端的对象上的方法。
    /// 主要场景包括：
    /// - 观察者模式，Grain 向客户端通知事件
    /// - 长时间运行操作的客户端回调
    /// - 轮询模式，Grain 从客户端拉取数据
    /// 可寻址的客户端对象必须注册到运行时，且生命周期有限。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ClientAddressableTests : HostedTestClusterEnsureDefaultStarted
    {
        private object anchor = null!;
        private readonly IRuntimeClient runtimeClient;

        /// <summary>
        /// 可由 Grain 调用的客户端对象的测试实现。
        /// 展示多种模式，包括：
        /// - 成功的方法执行（OnHappyPath）
        /// - 错误传播（OnSadPath）
        /// - 线程安全的状态管理（OnSerialStress）
        /// - 并发访问处理（OnParallelStress）
        /// </summary>
        private class MyPseudoGrain : IClientAddressableTestClientObject
        {
            private int counter = 0;
            private readonly List<int> numbers = new List<int>();

            public Task<string> OnHappyPath(string message)
            {
                if (string.IsNullOrEmpty(message))
                    throw new ArgumentException("target");
                else
                    return Task.FromResult(message);
            }

            public Task OnSadPath(string message)
            {
                if (string.IsNullOrEmpty(message))
                    throw new ArgumentException("target");
                else
                    throw new ApplicationException(message);
            }

            public Task<int> OnSerialStress(int n)
            {
                Assert.Equal(this.counter, n);
                ++this.counter;
                return Task.FromResult(10000 + n);
            }

            public Task<int> OnParallelStress(int n)
            {
                this.numbers.Add(n);
                return Task.FromResult(10000 + n);
            }

            public void VerifyNumbers(int iterationCount)
            {
                Assert.Equal(iterationCount, this.numbers.Count);
                this.numbers.Sort();
                for (var i = 0; i < this.numbers.Count; ++i)
                    Assert.Equal(i, this.numbers[i]);
            }
        }

        /// <summary>
        /// 客户端生产者的测试实现，Grain 可以轮询它以获取数据。
        /// 展示 Grain 主动从客户端请求数据的拉取模型。
        /// </summary>
        private class MyProducer : IClientAddressableTestProducer
        {
            private int counter = 0;

            public Task<int> Poll()
            {
                ++this.counter;
                return Task.FromResult(this.counter);
            }
        }

        public ClientAddressableTests(DefaultClusterFixture fixture) : base(fixture)
        {
            this.runtimeClient = this.HostedCluster.ServiceProvider.GetRequiredService<IRuntimeClient>();
        }

        /// <summary>
        /// 测试 Grain 到客户端的方法成功调用。
        /// 验证以下情况：
        /// - 客户端对象可以注册，并由 Grain 引用
        /// - Grain 能够成功调用客户端对象的方法
        /// - 返回值能够正确封送回 Grain
        /// - 对象引用能够得到正确清理
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ClientAddressable")]
        public async Task TestClientAddressableHappyPath()
        {
            var myOb = new MyPseudoGrain();
            this.anchor = myOb;
            var myRef = ((IInternalGrainFactory)this.GrainFactory).CreateObjectReference<IClientAddressableTestClientObject>(myOb);
            var proxy = this.GrainFactory.GetGrain<IClientAddressableTestGrain>(GetRandomGrainId());
            const string expected = "o hai!";
            await proxy.SetTarget(myRef);
            var actual = await proxy.HappyPath(expected);
            Assert.Equal(expected, actual);

            this.runtimeClient.DeleteObjectReference(myRef);
        }

        /// <summary>
        /// 测试错误从客户端对象传播回 Grain。
        /// 验证客户端方法引发的异常得到正确
        /// 序列化，并在调用方 Grain 中重新引发，同时保留错误消息。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ClientAddressable")]
        public async Task TestClientAddressableSadPath()
        {
            const string message = "o hai!";

            var myOb = new MyPseudoGrain();
            this.anchor = myOb;
            var myRef = ((IInternalGrainFactory)this.GrainFactory).CreateObjectReference<IClientAddressableTestClientObject>(myOb);
            var proxy = this.GrainFactory.GetGrain<IClientAddressableTestGrain>(GetRandomGrainId());
            await proxy.SetTarget(myRef);

            await Assert.ThrowsAsync<ApplicationException>(() =>
                proxy.SadPath(message)
            );

            this.runtimeClient.DeleteObjectReference(myRef);
        }

        /// <summary>
        /// 测试 Grain 从客户端对象请求数据的拉取模式。
        /// 验证以下情况：
        /// - 客户端生产者对象可通过会合 Grain 在多个 Grain 间共享
        /// - 消费者 Grain 可以从客户端生产者拉取数据
        /// - 客户端对象的状态在多次调用间得到正确维护
        /// 此模式适用于客户端生成数据、Grain 消费数据的场景。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ClientAddressable")]
        public async Task GrainShouldSuccessfullyPullFromClientObject()
        {
            var myOb = new MyProducer();
            this.anchor = myOb;
            var myRef = ((IInternalGrainFactory)this.GrainFactory).CreateObjectReference<IClientAddressableTestProducer>(myOb);
            var rendez = this.GrainFactory.GetGrain<IClientAddressableTestRendezvousGrain>(0);
            var consumer = this.GrainFactory.GetGrain<IClientAddressableTestConsumer>(0);

            await rendez.SetProducer(myRef);
            await consumer.Setup();
            var n = await consumer.PollProducer();
            Assert.Equal(1, n);

            this.runtimeClient.DeleteObjectReference(myRef);
        }

        /// <summary>
        /// 对大量客户端对象调用的串行执行进行压力测试。
        /// 验证以下情况：
        /// - 客户端对象在大量顺序调用中保持正确状态
        /// - Orleans 正确地串行化对客户端对象的访问
        /// - 串行执行模型中不存在竞态条件。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ClientAddressable")]
        public async Task MicroClientAddressableSerialStressTest()
        {
            const int iterationCount = 1000;

            var myOb = new MyPseudoGrain();
            this.anchor = myOb;
            var myRef = ((IInternalGrainFactory)this.GrainFactory).CreateObjectReference<IClientAddressableTestClientObject>(myOb);
            var proxy = this.GrainFactory.GetGrain<IClientAddressableTestGrain>(GetRandomGrainId());
            await proxy.SetTarget(myRef);
            await proxy.MicroSerialStressTest(iterationCount);

            this.runtimeClient.DeleteObjectReference(myRef);
        }

        /// <summary>
        /// 对大量客户端对象调用的并行执行进行压力测试。
        /// 验证以下情况：
        /// - 客户端对象可以处理来自 Grain 的并发访问
        /// - 所有并行调用均得到处理且不丢失数据
        /// - 客户端对象保持线程安全
        /// 此测试验证 Orleans 处理高吞吐量客户端回调的能力。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ClientAddressable")]
        public async Task MicroClientAddressableParallelStressTest()
        {
            const int iterationCount = 1000;

            var myOb = new MyPseudoGrain();
            this.anchor = myOb;
            var myRef = ((IInternalGrainFactory)this.GrainFactory).CreateObjectReference<IClientAddressableTestClientObject>(myOb);
            var proxy = this.GrainFactory.GetGrain<IClientAddressableTestGrain>(GetRandomGrainId());
            await proxy.SetTarget(myRef);
            await proxy.MicroParallelStressTest(iterationCount);

            this.runtimeClient.DeleteObjectReference(myRef);

            myOb.VerifyNumbers(iterationCount);
        }
    }
}

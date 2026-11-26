using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 在 Orleans中测试客户端可处理对象,允许 grains 回调到客户端对象。
    /// 这个功能可以进行双向通信, grains 可在客户主机上对客服物体使用方法。
    /// 关键设想包括:
    /// - grains 通知客户事件观察模式
    /// - 对长期业务的客户-用户方面回调
    /// - grains 从客户中提取数据的投票模式
    /// 可客户处理的物体必须在运行时登记,并有有限的寿命。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ClientAddressableTests : HostedTestClusterEnsureDefaultStarted
    {
        private object anchor = null!;
        private readonly IRuntimeClient runtimeClient;

        /// <summary>
        /// 测试可称为 grains的客户端对象的测试实施。
        /// 展示了各种模式,包括:
        /// - 成功方法执行(OnHappyPath)
        /// - 错误传播(OnSadPath)
        /// - 条安全国家管理(OnSerialStress)
        /// - 同时进入处理(OnParallelStress)
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
        /// 测试用户方生产商的 grains 数据调查实施情况。
        /// 演示 grains 积极向客户索取数据的拉动模型。
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
        /// 测试成功 grain-客户对方法的调试。
        /// 证明:
        /// - grains可以登记客户对象并注明其编号
        /// - Grains 能够成功地对客户对象使用方法
        /// - 返回值被适当调回 grain
        /// - 目标参考材料可以适当清理
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
        /// 测试错误 从客户端对象传播回 grains。
        /// 证明客户方方法中出现的例外情形是适当的
        /// 序列后重新投放到调用 grain中,保留错误消息。
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
        /// 测试 grains 向客户端对象请求数据的拉动模式。
        /// 证明:
        /// - 客户生产对象可以由用户和客户共同使用 grains 经由会合 grain
        /// - 消费者 grains 能够从客户方生产者那里提取数据
        /// - 电话往来客户对象中正确维护国家
        /// 这种模式对于客户生成 grains 消费数据的设想方案有用。
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
        /// 压力测试许多客户的物体引用的连续处决。
        /// 证明:
        /// - 客户对象在许多连续调用中保持正确状态
        /// - Orleans 妥善地对客户对象的存取进行序列记录
        /// - 连环处决模式中没有出现种族条件
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
        /// 压力测试同时执行许多客户对象的用途。
        /// 证明:
        /// - 客户对象可同时处理 grains的存取
        /// - 所有平行工作处理均不丢失数据
        /// - 客户对象的线条安全
        /// 测试 Orleans处理高传输量客户端回调的能力。
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

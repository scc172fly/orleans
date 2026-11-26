using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.TestingHost;
using Tester;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 查询请求的测试,通过 grain 电话和活动进行背景传播。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class RequestContextTests_Silo : OrleansTestingBase, IClassFixture<RequestContextTests_Silo.Fixture>, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly Fixture fixture;
        private readonly OutsideRuntimeClient runtimeClient;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
            }
        }

        public RequestContextTests_Silo(ITestOutputHelper output, Fixture fixture)
        {
            this.output = output;
            this.fixture = fixture;
            this.runtimeClient = this.fixture.Client.ServiceProvider.GetRequiredService<OutsideRuntimeClient>();

            RequestContextTestUtils.ClearActivityId();
            RequestContext.Clear();
        }

        public void Dispose()
        {
            RequestContextTestUtils.ClearActivityId();
            RequestContext.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_Simple()
        {
            Guid activityId = Guid.NewGuid();
            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            RequestContextTestUtils.SetActivityId(activityId);
            Guid result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId 未正确传播
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_AC_Test1()
        {
            long id = GetRandomGrainId();
            const string key = "TraceId";
            string val = "TraceValue-" + id;
            string val2 = val + "-2";

            var grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(id);

            RequestContext.Set(key, val);
            var result = await grain.TraceIdEcho();
            Assert.Equal(val, result);  // "即刻" RequestContext 反响不正确 。

            RequestContext.Set(key, val2);
            result = await grain.TraceIdDoubleEcho();
            Assert.Equal(val2, result);  // “ 过渡 RequestContext 回声不正确”

            RequestContext.Set(key, val);
            result = await grain.TraceIdDelayedEcho1();
            Assert.Equal(val, result); // “延迟(StartNew) RequestContext 回声不正确”);

            RequestContext.Set(key, val2);
            result = await grain.TraceIdDelayedEcho2();
            Assert.Equal(val2, result); // “延迟(ContinueWith) RequestContext 回声不正确”);
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_Task_Test1()
        {
            long id = GetRandomGrainId();
            const string key = "TraceId";
            string val = "TraceValue-" + id;
            string val2 = val + "-2";

            var grain = this.fixture.GrainFactory.GetGrain<IRequestContextTaskGrain>(id);

            RequestContext.Set(key, val);
            var result = await grain.TraceIdEcho();
            Assert.Equal(val, result);  // "即刻" RequestContext 反响不正确 。

            RequestContext.Set(key, val2);
            result = await grain.TraceIdDoubleEcho();
            Assert.Equal(val2, result);  // “ 过渡 RequestContext 回声不正确”

            RequestContext.Set(key, val);
            result = await grain.TraceIdDelayedEcho1();
            Assert.Equal(val, result); // “延迟(StartNew) RequestContext 回声不正确”);

            RequestContext.Set(key, val2);
            result = await grain.TraceIdDelayedEcho2();
            Assert.Equal(val2, result); // “延迟(ContinueWith) RequestContext 回声不正确”);

            RequestContext.Set(key, val);
            result = await grain.TraceIdDelayedEchoAwait();
            Assert.Equal(val, result); // ”(等待) RequestContext 回声不正确”;

            // 预期行为为预期行为, 无法工作, 因为设计 Task.Run 时没有使用 Orleans 任务调度器
            //RequestContext.Set(key, val2);
            //结果=等待 grain.TraceIdDelayedEchoTaskRun();
            //Assert.Equal(val2, 结果); // " 延迟(Task.Run) RequestContext echo was not correct");
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_Task_TestRequestContext()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IRequestContextTaskGrain>(1);
            Tuple<string, string> requestContext = await grain.TestRequestContext();
            this.fixture.Logger.LogInformation("Request Context is: {RequestContext}", requestContext);
            Assert.Equal("binks", requestContext.Item1);  // “项目1=”+ requestContext.Item1
            Assert.Equal("binks", requestContext.Item2);  // “项目2=”+ requestContext.Item2
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_RC_Set_E2E()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();
            Guid nullActivityId = Guid.Empty;

            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            RequestContext.Set(RequestContext.CALL_CHAIN_REENTRANCY_HEADER, activityId);
            Guid result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId 未正确传播
            RequestContext.Clear();

            RequestContext.Set(RequestContext.CALL_CHAIN_REENTRANCY_HEADER, nullActivityId);
            result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            RequestContext.Clear();

            RequestContext.Set(RequestContext.CALL_CHAIN_REENTRANCY_HEADER, activityId2);
            result = await grain.E2EActivityId();
            Assert.Equal(activityId2, result);  // E2E ActivityId 2 传播不正确
            RequestContext.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_CM_E2E()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();
            Guid nullActivityId = Guid.Empty;

            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            RequestContext.ReentrancyId = activityId;
            Guid result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId 未正确传播
            RequestContext.Clear();

            RequestContext.ReentrancyId = nullActivityId;
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                result = await grain.E2EActivityId();
                Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            }
            RequestContext.Clear();

            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            RequestContext.ReentrancyId = activityId2;
            result = await grain.E2EActivityId();
            Assert.Equal(activityId2, result);  // E2E ActivityId 2 传播不正确
            RequestContext.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_CM_E2E_ViaProxy()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();
            Guid nullActivityId = Guid.Empty;

            IRequestContextProxyGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextProxyGrain>(GetRandomGrainId());

            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            RequestContext.ReentrancyId = activityId;
            Guid result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId 未正确传播
            RequestContext.Clear();

            RequestContext.ReentrancyId = nullActivityId;
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                result = await grain.E2EActivityId();
                Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            }
            RequestContext.Clear();

            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            RequestContext.ReentrancyId = activityId2;
            result = await grain.E2EActivityId();
            Assert.Equal(activityId2, result);  // E2E ActivityId 2 传播不正确
            RequestContext.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_RC_None_E2E()
        {
            Guid nullActivityId = Guid.Empty;

            RequestContext.Clear();

            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            Guid result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // “E2E ActivityId 不应存在”

            result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            RequestContext.Clear();

            result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // “E2E ActivityId 2 不应存在”
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));  // “ 不应设置 ActivityId 上下文”

            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));  // “ 不应设置 ActivityId 上下文”

                result = await grain.E2EActivityId();

                Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"

                RequestContext.Clear();
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_CM_None_E2E()
        {
            Guid nullActivityId = Guid.Empty;

            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            Guid result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // “E2E ActivityId 不应存在”

            RequestContextTestUtils.SetActivityId(nullActivityId);
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            RequestContext.Clear();

            RequestContextTestUtils.SetActivityId(nullActivityId);
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                result = await grain.E2EActivityId();
                Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            }
            RequestContext.Clear();
            RequestContextTestUtils.SetActivityId(nullActivityId);
            Assert.Null(RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER));
            result = await grain.E2EActivityId();
            Assert.Equal(nullActivityId, result);  // "Null ActivityId 传播E2E错误"
            RequestContext.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_ActivityId_CM_DynamicChange_Client()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();

            IRequestContextTestGrain grain = this.fixture.GrainFactory.GetGrain<IRequestContextTestGrain>(GetRandomGrainId());

            RequestContext.ReentrancyId = activityId;
            Guid result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId # 1 未正确传播 。
            RequestContext.Clear();

            using (RequestContext.SuppressCallChainReentrancy())
            {
                result = await grain.E2EActivityId();
                Assert.Equal(Guid.Empty, result);  // E2E ActivityId # 2 未被传播
                RequestContext.Clear();
            }

            RequestContext.ReentrancyId = activityId2;
            result = await grain.E2EActivityId();
            Assert.Equal(activityId2, result);  // E2E ActivityId #2应该已经传播
            RequestContext.Clear();

            RequestContext.ReentrancyId = activityId;
            result = await grain.E2EActivityId();
            Assert.Equal(activityId, result);  // E2E ActivityId #1 在#2之后没有正确传播
            RequestContext.Clear();
        }
    }

    internal class RequestContextGrainObserver : ISimpleGrainObserver
    {
        private readonly ITestOutputHelper output;
        private readonly Action<int, int, object> action;
        private readonly object result;

        public RequestContextGrainObserver(ITestOutputHelper output, Action<int, int, object> action, object result)
        {
            this.output = output;
            this.action = action;
            this.result = result;
        }

        public void StateChanged(int a, int b)
        {
            output.WriteLine("RequestContextGrainObserver.StateChanged a={0} b={1}", a, b);
            this.action?.Invoke(a, b, this.result);
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class Halo_RequestContextTests : OrleansTestingBase
    {
        private readonly ITestOutputHelper output;

        public Halo_RequestContextTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task Halo_RequestContextShouldBeMaintainedWhenThreadHoppingOccurs()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            int numTasks = 20;
            Task[] tasks = new Task[numTasks];

            for (int i = 0; i < numTasks; i++)
            {
                var closureInt = i;
                async Task func() { await ContextTester(closureInt, cancellationToken); }
                tasks[i] = Task.Run(func, cancellationToken);
            }

            await Task.WhenAll(tasks);
        }

        private async Task ContextTester(int i, CancellationToken cancellationToken)
        {
            RequestContext.Set("threadId", i);
            int contextId = (int)(RequestContext.Get("threadId") ?? -1);
            output.WriteLine("ExplicitId={0}, ContextId={2}, ManagedThreadId={1}", i, Environment.CurrentManagedThreadId, contextId);
            await FrameworkContextVerification(i, cancellationToken).ConfigureAwait(false);
        }

        private async Task FrameworkContextVerification(int id, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(10, cancellationToken);
                int contextId = (int)(RequestContext.Get("threadId") ?? -1);
                output.WriteLine("Inner, in loop {0}, Explicit Id={2}, ContextId={3}, ManagedThreadId={1}", i, Environment.CurrentManagedThreadId, id, contextId);
                Assert.Equal(id, contextId);
            }
        }
    }

    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class Halo_CallContextTests : OrleansTestingBase
    {
        private readonly ITestOutputHelper output;

        private readonly AsyncLocal<int> threadId = new AsyncLocal<int>();

        public Halo_CallContextTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task Halo_LogicalCallContextShouldBeMaintainedWhenThreadHoppingOccurs()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            int numTasks = 20;
            Task[] tasks = new Task[numTasks];

            for (int i = 0; i < numTasks; i++)
            {
                var closureInt = i;
                async Task func() { await ContextTester(closureInt, cancellationToken); }
                tasks[i] = Task.Run(func, cancellationToken);
            }

            await Task.WhenAll(tasks);
        }

        private async Task ContextTester(int i, CancellationToken cancellationToken)
        {
            threadId.Value = i;
            int contextId = threadId.Value;
            output.WriteLine("ExplicitId={0}, ContextId={2}, ManagedThreadId={1}", i, Environment.CurrentManagedThreadId, contextId);
            await FrameworkContextVerification(i, cancellationToken).ConfigureAwait(false);
        }

        private async Task FrameworkContextVerification(int id, CancellationToken cancellationToken)
        {
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(10, cancellationToken);
                int contextId = threadId.Value;
                output.WriteLine("Inner, in loop {0}, Explicit Id={2}, ContextId={3}, ManagedThreadId={1}", i, Environment.CurrentManagedThreadId, id, contextId);
                Assert.Equal(id, contextId);
            }
        }
    }
}

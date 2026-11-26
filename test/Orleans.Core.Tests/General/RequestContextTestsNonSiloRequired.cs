using System.Globalization;
using Orleans;
using Orleans.Runtime;
using TestExtensions;
using Xunit;
using Tester;

namespace UnitTests.General
{
    /// <summary>
    /// 请求中环境功能测试,包括多行、活动识别传播和进出口业务。
    /// </summary>
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class RequestContextTests_Local : IDisposable
    {
        private readonly Dictionary<string, object> headers = new Dictionary<string, object>();


        private readonly TestEnvironmentFixture fixture;

        public RequestContextTests_Local(TestEnvironmentFixture fixture)
        {
            this.fixture = fixture;
            RequestContext.Clear();
            headers.Clear();
        }

        public void Dispose()
        {
            TestCleanup();
        }

        private void TestCleanup()
        {
            RequestContextTestUtils.ClearActivityId();
            RequestContext.Clear();
            headers.Clear();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_MultiThreads_ExportToMessage()
        {
            const int NumLoops = 50;
            string id = "key" + Random.Shared.Next();

            Message msg = new Message();
            Task[] promises = new Task[NumLoops];
            ManualResetEventSlim flag = new ManualResetEventSlim(false);
            for (int i = 0; i < NumLoops; i++)
            {
                string key = id + "-" + i;
                RequestContext.Set(key, i);
                promises[i] = Task.Run(() =>
                {
                    flag.Wait(TestContext.Current.CancellationToken);
                    msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
                }, TestContext.Current.CancellationToken);
                flag.Set();
                Thread.Sleep(1);
                RequestContext.Remove(key);
            }
            await Task.WhenAll(promises);
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public void RequestContext_ActivityId_ExportToMessage()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();
            Guid nullActivityId = Guid.Empty;

            Message msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            if (msg.RequestContextData != null) foreach (var kvp in msg.RequestContextData)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.False(headers.ContainsKey(RequestContext.CALL_CHAIN_REENTRANCY_HEADER), "ActivityId should not be be present " + headers.ToStrings(separator: ","));
            TestCleanup();

            RequestContext.ReentrancyId = activityId;
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            if (msg.RequestContextData != null) foreach (var kvp in msg.RequestContextData)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.True(headers.ContainsKey(RequestContext.CALL_CHAIN_REENTRANCY_HEADER), "ActivityId #1 should be present " + headers.ToStrings(separator: ","));
            object result = headers[RequestContext.CALL_CHAIN_REENTRANCY_HEADER];
            Assert.NotNull(result);// ActivityId # 1 不应该无效
            Assert.Equal(activityId, result);  // E2E ActivityId # 1 未正确传播 。
            Assert.Equal(activityId, RequestContextTestUtils.GetActivityId());  // "原件E2E ActivityId #1本不应该改变"
            TestCleanup();

            RequestContextTestUtils.SetActivityId(nullActivityId);
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            if (msg.RequestContextData != null) foreach (var kvp in msg.RequestContextData)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.False(headers.ContainsKey(RequestContext.CALL_CHAIN_REENTRANCY_HEADER), "Null ActivityId should not be present " + headers.ToStrings(separator: ","));
            TestCleanup();

            RequestContextTestUtils.SetActivityId(activityId2);
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            foreach (var kvp in msg.RequestContextData!)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.True(headers.ContainsKey(RequestContext.CALL_CHAIN_REENTRANCY_HEADER), "ActivityId #2 should be present " + headers.ToStrings(separator: ","));
            result = headers[RequestContext.CALL_CHAIN_REENTRANCY_HEADER];
            Assert.NotNull(result); // ActivityId #2 不应该无效
            Assert.Equal(activityId2, result);  // E2E ActivityId # 2 未正确传播

            Assert.Equal(activityId2, RequestContextTestUtils.GetActivityId());  // "原件E2E ActivityId #2本不应该改变"
            TestCleanup();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public void RequestContext_ActivityId_ExportImport()
        {
            Guid activityId = Guid.NewGuid();
            Guid activityId2 = Guid.NewGuid();
            Guid nullActivityId = Guid.Empty;

            Message msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            RequestContext.Clear();
            RequestContextExtensions.Import(msg.RequestContextData);
            var actId = RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER);
            Assert.Null(actId);
            TestCleanup();

            RequestContextTestUtils.SetActivityId(activityId);
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            RequestContext.Clear();
            RequestContextExtensions.Import(msg.RequestContextData);
            actId = RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER);
            if (msg.RequestContextData != null) foreach (var kvp in msg.RequestContextData)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.NotNull(actId); // "ActivityId #1应该存在 " + headers.ToStrings(分隔符:")
            object result = headers[RequestContext.CALL_CHAIN_REENTRANCY_HEADER];
            Assert.NotNull(result);// "ActivityId #1 不应该无效"
            Assert.Equal(activityId, result);  // E2E ActivityId # 1 未正确传播 。
            Assert.Equal(activityId, RequestContextTestUtils.GetActivityId());  // "原件E2E ActivityId #1本不应该改变"
            TestCleanup();

            RequestContextTestUtils.SetActivityId(nullActivityId);
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            RequestContext.Clear();
            RequestContextExtensions.Import(msg.RequestContextData);
            actId = RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER);
            Assert.Null(actId);
            TestCleanup();

            RequestContextTestUtils.SetActivityId(activityId2);
            msg = new Message();
            msg.RequestContextData = RequestContextExtensions.Export(this.fixture.DeepCopier);
            RequestContext.Clear();
            RequestContextExtensions.Import(msg.RequestContextData);
            actId = RequestContext.Get(RequestContext.CALL_CHAIN_REENTRANCY_HEADER);
            if (msg.RequestContextData != null) foreach (var kvp in msg.RequestContextData)
            {
                headers.Add(kvp.Key, kvp.Value);
            }
            ;
            Assert.NotNull(actId); // "ActivityId #2应该存在 " + headers.ToStrings(分隔符:")
            result = headers[RequestContext.CALL_CHAIN_REENTRANCY_HEADER];
            Assert.NotNull(result); // "ActivityId #2不应该是无效的"
            Assert.Equal(activityId2, result);// E2E ActivityId #2没有正确传播

            Assert.Equal(activityId2, RequestContextTestUtils.GetActivityId()); // "原件E2E ActivityId #2本不应该改变"
            TestCleanup();
        }

        [Fact, TestCategory("Functional"), TestCategory("RequestContext")]
        public async Task RequestContext_CrossThread()
        {
            const int NumLoops = 1000;

            string name1 = "Name" + Random.Shared.Next();
            string data1 = "Main";

            RequestContext.Set(name1, data1);
            Assert.Equal(data1, RequestContext.Get(name1));  // "RC.GetData-主"

            Task[] promises = new Task[NumLoops];
            for (int i = 0; i < NumLoops; i++)
            {
                string str = i.ToString(CultureInfo.InvariantCulture);
                promises[i] = Task.Run(async () =>
                {
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                    Assert.Equal(data1, RequestContext.Get(name1));  // "RC.GetData-Task.Run-0"
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                    // 设定新值
                    RequestContext.Set(name1, str);
                    Assert.Equal(str, RequestContext.Get(name1));  // “RC.GetData-Task.Run-1-”+吨
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                    Assert.Equal(str, RequestContext.Get(name1));  // “RC.GetData-Task.Run-2”+字符
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                    Assert.Equal(str, RequestContext.Get(name1));  // "RC.GetData-Task.Run-3 -"+吨
                }, TestContext.Current.CancellationToken);
            }
            await Task.WhenAll(promises);
            Assert.Equal(data1, RequestContext.Get(name1));  // "RC.GetData- 终极主"
        }

    }

}

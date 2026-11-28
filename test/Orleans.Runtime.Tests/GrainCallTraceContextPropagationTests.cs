using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Orleans;
using Orleans.Core.Internal;
using Orleans.Diagnostics;
using Orleans.Placement;
using Orleans.Runtime.Placement;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 专门验证客户端与 Grain 服务器之间跟踪上下文传播的测试。
    /// 这些测试揭示服务器端跟踪跨度开启新跟踪而非继续客户端跟踪的问题。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [Collection("ActivationTracing")]
    public class GrainCallTraceContextPropagationTests : OrleansTestingBase, IClassFixture<ActivationTracingTests.Fixture>
    {
        private static readonly ConcurrentBag<Activity> Started = new();
        private static readonly ActivityListener Listener;

        static GrainCallTraceContextPropagationTests()
        {
            Listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        || src.Name == ActivitySources.LifecycleActivitySourceName
                                        || src.Name == ActivitySources.StorageActivitySourceName,
                Sample = (ref _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => Started.Add(activity),
            };
            ActivitySource.AddActivityListener(Listener);
        }

        private readonly ActivationTracingTests.Fixture _fixture;
        private readonly ITestOutputHelper _output;

        public GrainCallTraceContextPropagationTests(ActivationTracingTests.Fixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// 关键测试：验证服务器端 Grain 调用的 Activity 与客户端具有相同的 TraceId。
        /// 如果跟踪上下文传播失败，此测试将失败，因为服务器会启动新跟踪，
        /// 而不是继续客户端跟踪。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerSideGrainCallSharesSameTraceIdAsClient()
        {
            Started.Clear();

            // 在客户端启动父 Activity
            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-parent-activity");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();
            var clientSpanId = clientActivity.SpanId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");
            _output.WriteLine($"Client SpanId: {clientSpanId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());

                // 此调用应将跟踪上下文传播到服务器
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server SpanId: {serverTraceInfo.SpanId}");
                _output.WriteLine($"Server ParentSpanId: {serverTraceInfo.ParentSpanId}");
                _output.WriteLine($"Server ParentId: {serverTraceInfo.ParentId}");
                _output.WriteLine($"Server OperationName: {serverTraceInfo.OperationName}");
                _output.WriteLine($"Server Kind: {serverTraceInfo.Kind}");
                _output.WriteLine($"Server IsRemote: {serverTraceInfo.IsRemote}");
                _output.WriteLine($"Server TraceParentFromRequestContext: {serverTraceInfo.TraceParentFromRequestContext}");

                // 关键断言：服务器必须具有 Activity
                Assert.True(serverTraceInfo.HasActivity, "Server-side grain call should have an Activity.Current");

                // 关键断言：服务器 TraceId 必须与客户端 TraceId 匹配
                // 如果此断言失败，则说明跟踪上下文传播已损坏！
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        [Fact]
        [TestCategory("BVT")]
        public async Task GrainCallUsesCurrentRpcSemanticConventionKeys()
        {
            Started.Clear();

            var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
            await grain.GetTraceContextInfo();

            var activity = Started.FirstOrDefault(
                a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                    && a.GetTagItem("rpc.system.name") is not null);

            Assert.NotNull(activity);
            Assert.Equal("orleans", activity.GetTagItem("rpc.system.name"));
            Assert.Equal(typeof(ITraceContextPropagationGrain).FullName, activity.GetTagItem("orleans.rpc.service"));
            Assert.Equal(
                $"{typeof(ITraceContextPropagationGrain).FullName}/{nameof(ITraceContextPropagationGrain.GetTraceContextInfo)}",
                activity.GetTagItem("rpc.method"));
            Assert.Null(activity.GetTagItem("rpc.system"));
            Assert.Null(activity.GetTagItem("rpc.service"));
            Assert.NotNull(activity.GetTagItem("orleans.rpc.target_id"));
        }

        /// <summary>
        /// 验证服务器端 Activity 的类型为 Server，并具有远程父级。
        /// 这确认 W3C 跟踪上下文已得到正确处理。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerSideActivityHasCorrectKindAndRemoteParent()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-activity-kind-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server Kind: {serverTraceInfo.Kind}");
                _output.WriteLine($"Server IsRemote: {serverTraceInfo.IsRemote}");

                Assert.True(serverTraceInfo.HasActivity, "Server-side grain call should have an Activity.Current");

                // 服务器端 Activity 的类型应为 Server
                Assert.Equal(ActivityKind.Server, serverTraceInfo.Kind);

                // 服务器端 Activity 应具有远程父级（从客户端传播而来）
                Assert.True(serverTraceInfo.IsRemote, "Server-side activity should have HasRemoteParent=true");
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证嵌套 Grain 间调用的跟踪上下文传播。
        /// 调用链中的所有调用都应共享相同的 TraceId。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task NestedGrainCallsShareSameTraceId()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-nested-calls-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var (localInfo, nestedInfo) = await grain.GetNestedTraceContextInfo();

                _output.WriteLine($"First Grain TraceId: {localInfo.TraceId}");
                _output.WriteLine($"Nested Grain TraceId: {nestedInfo.TraceId}");

                // 两个 Grain 都应具有 Activity
                Assert.True(localInfo.HasActivity, "First grain should have an Activity.Current");
                Assert.True(nestedInfo.HasActivity, "Nested grain should have an Activity.Current");

                // 关键：所有调用都应共享相同的 TraceId
                Assert.Equal(clientTraceId, localInfo.TraceId);
                Assert.Equal(clientTraceId, nestedInfo.TraceId);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证发起 Grain 调用时 RequestContext 中正确设置了 traceparent 标头。
        /// 这测试出站筛选器注入跟踪上下文的行为。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceParentIsSetInRequestContext()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-traceparent-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceParentFromRequestContext: {serverTraceInfo.TraceParentFromRequestContext}");

                // RequestContext 中应存在 traceparent 标头
                Assert.NotNull(serverTraceInfo.TraceParentFromRequestContext);
                Assert.NotEmpty(serverTraceInfo.TraceParentFromRequestContext);

                // traceparent 应包含客户端的 TraceId
                Assert.Contains(clientTraceId, serverTraceInfo.TraceParentFromRequestContext);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证客户端出站跟踪跨度与服务器端入站跟踪跨度之间正确关联。
        /// 服务器跟踪跨度的父级应为客户端出站跟踪跨度。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ClientAndServerSpansAreProperlyLinked()
        {
            Started.Clear();

            using var clientParentActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-linking-test");
            clientParentActivity?.Start();

            Assert.NotNull(clientParentActivity);
            var clientTraceId = clientParentActivity.TraceId.ToString();

            _output.WriteLine($"Client Parent TraceId: {clientTraceId}");
            _output.WriteLine($"Client Parent SpanId: {clientParentActivity.SpanId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                _ = await grain.GetTraceContextInfo();

                // 查找客户端出站跟踪跨度（应为测试 Activity 的子级）
                var clientOutgoingSpan = Started
                    .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                               && a.Kind == ActivityKind.Client
                               && a.OperationName.Contains("GetTraceContextInfo"))
                    .FirstOrDefault();

                // 查找服务器端入站跟踪跨度
                var serverIncomingSpan = Started
                    .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                               && a.Kind == ActivityKind.Server
                               && a.OperationName.Contains("GetTraceContextInfo"))
                    .FirstOrDefault();

                _output.WriteLine($"Client Outgoing Span: {clientOutgoingSpan?.Id ?? "(not found)"}");
                _output.WriteLine($"Server Incoming Span: {serverIncomingSpan?.Id ?? "(not found)"}");

                Assert.NotNull(clientOutgoingSpan);
                Assert.NotNull(serverIncomingSpan);

                // 两者应共享相同的 TraceId
                Assert.Equal(clientTraceId, clientOutgoingSpan.TraceId.ToString());
                Assert.Equal(clientTraceId, serverIncomingSpan.TraceId.ToString());

                // 客户端出站跟踪跨度应以测试 Activity 为父级
                Assert.Equal(clientParentActivity.SpanId.ToString(), clientOutgoingSpan.ParentSpanId.ToString());

                // 服务器跟踪跨度的父级应为客户端出站跟踪跨度
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverIncomingSpan.ParentSpanId.ToString());
            }
            finally
            {
                clientParentActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证即使客户端没有活动的 Activity，也能正确传播跟踪上下文。
        /// 在这种情况下，服务器仍应创建自身的跟踪。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerCreatesOwnTraceWhenClientHasNoActivity()
        {
            Started.Clear();

            // 确保客户端没有当前 Activity
            var previousActivity = Activity.Current;
            Activity.Current = null;

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");

                // 服务器仍应创建 Activity（开启新的跟踪）
                Assert.True(serverTraceInfo.HasActivity, "Server should create an activity even when client has none");
                Assert.NotNull(serverTraceInfo.TraceId);
                Assert.NotEmpty(serverTraceInfo.TraceId);
            }
            finally
            {
                Activity.Current = previousActivity;
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证来自同一个客户端 Activity 的多个并发 Grain 调用
        /// 均共享相同的 TraceId。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ConcurrentGrainCallsShareSameTraceId()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-concurrent-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                var tasks = Enumerable.Range(0, 5)
                    .Select(i => _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next()).GetTraceContextInfo())
                    .ToList();

                var results = await Task.WhenAll(tasks);

                foreach (var (result, index) in results.Select((r, i) => (r, i)))
                {
                    _output.WriteLine($"Grain {index} TraceId: {result.TraceId}");

                    Assert.True(result.HasActivity, $"Grain {index} should have an Activity.Current");
                    Assert.Equal(clientTraceId, result.TraceId);
                }
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 边缘情况：测试 traceparent 标头包含意外格式时的跟踪上下文传播。
        /// 服务器应妥善处理格式错误的标头，并仍能创建 Activity。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerHandlesMalformedTraceParentGracefully()
        {
            Started.Clear();

            // 在 RequestContext 中手动设置无效的 traceparent
            RequestContext.Set("traceparent", "invalid-traceparent-value");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server TraceParentFromRequestContext: {serverTraceInfo.TraceParentFromRequestContext}");

                // 服务器仍应具有 Activity（创建新的跟踪）
                Assert.True(serverTraceInfo.HasActivity, "Server should create an activity even with malformed traceparent");
                Assert.NotNull(serverTraceInfo.TraceId);
            }
            finally
            {
                RequestContext.Clear();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试 RequestContext 中的 traceparent 表示客户端出站跟踪跨度，
        /// 而非原始父 Activity。这验证了客户端正确创建跟踪跨度。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceParentReflectsClientOutgoingSpan()
        {
            Started.Clear();

            using var clientParentActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-traceparent-reflection-test");
            clientParentActivity?.Start();

            Assert.NotNull(clientParentActivity);
            var clientTraceId = clientParentActivity.TraceId.ToString();
            var clientParentSpanId = clientParentActivity.SpanId.ToString();

            _output.WriteLine($"Client Parent TraceId: {clientTraceId}");
            _output.WriteLine($"Client Parent SpanId: {clientParentSpanId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceParentFromRequestContext: {serverTraceInfo.TraceParentFromRequestContext}");
                _output.WriteLine($"Server ParentSpanId: {serverTraceInfo.ParentSpanId}");

                Assert.NotNull(serverTraceInfo.TraceParentFromRequestContext);

                // traceparent 应包含 TraceId
                Assert.Contains(clientTraceId, serverTraceInfo.TraceParentFromRequestContext);

                // 服务器父跟踪跨度的 ID 不应为客户端原始父跟踪跨度的 ID
                // 而应为客户端出站调用跟踪跨度的 ID
                // （因为客户端为出站调用创建新的跟踪跨度）
                Assert.NotEqual(clientParentSpanId, serverTraceInfo.ParentSpanId);

                // 查找客户端出站跟踪跨度
                var clientOutgoingSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Client
                                        && a.OperationName.Contains("GetTraceContextInfo"));

                Assert.NotNull(clientOutgoingSpan);

                // 服务器父跟踪跨度的 ID 应与客户端出站跟踪跨度的 ID 相匹配
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverTraceInfo.ParentSpanId);

                // traceparent 应包含客户端出站跟踪跨度的 ID
                Assert.Contains(clientOutgoingSpan.SpanId.ToString(), serverTraceInfo.TraceParentFromRequestContext);
            }
            finally
            {
                clientParentActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试在 Grain 的 OnActivateAsync 内发起调用时的跟踪上下文传播。
        /// 这是一个常见的边缘情况，即激活过程可能缺少正确的跟踪上下文。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceContextIsPropagatedDuringActivation()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-activation-context-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                // 发起触发激活的调用
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceId (during activation): {serverTraceInfo.TraceId}");

                // 验证跟踪 ID 匹配
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);

                // 查找激活 Span。
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    // 激活跟踪跨度也应共享相同的跟踪 ID
                    Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());
                }
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试 tracestate 标头是否与 traceparent 一起正确传播。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceStateIsPropagatedWithTraceParent()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-tracestate-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);

            // 在客户端 Activity 上设置 tracestate
            clientActivity.TraceStateString = "vendor1=value1,vendor2=value2";

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                // 查找服务器跟踪跨度
                var serverSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Server
                                        && a.OperationName.Contains("GetTraceContextInfo"));

                _output.WriteLine($"Server Span TraceState: {serverSpan?.TraceStateString ?? "(null)"}");

                // tracestate 应传播至服务器
                // 注意：可能需要根据 Orleans 处理 tracestate 的方式调整此测试
                if (serverSpan is not null && !string.IsNullOrEmpty(serverSpan.TraceStateString))
                {
                    Assert.Contains("vendor1", serverSpan.TraceStateString);
                }
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 关键测试：验证 Grain 调用触发激活时，激活跟踪跨度
        /// 与 Grain 调用共享同一 TraceId，并在跟踪中正确关联。
        /// 
        /// 此测试重现了一个生产问题：激活跨度启动了新的追踪，
        /// 而不是作为传入 Grain 调用追踪的一部分。
        /// 
        /// 预期的跟踪结构：
        ///   client-parent-activity
        ///     └── ITraceContextPropagationGrain/GetTraceContextInfo（客户端，出站）
        ///           └── ITraceContextPropagationGrain/GetTraceContextInfo（服务器，入站）
        ///                 └── activate grain（应关联至此次跟踪！）
        ///                       ├── register directory entry（注册目录条目）
        ///                       └── execute OnActivateAsync（执行 OnActivateAsync）
        /// 
        /// 缺陷场景（本测试要检查的情况）：
        ///   activate grain（新跟踪，与客户端断开关联！） ← 这就是缺陷
        ///     ├── register directory entry（注册目录条目）
        ///     └── execute OnActivateAsync（执行 OnActivateAsync）
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanSharesTraceIdWithTriggeringGrainCall()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-activation-trace-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                // 使用唯一 Grain ID，确保触发新的激活
                var uniqueGrainId = Random.Shared.Next();
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(uniqueGrainId);

                // 由于是新的 Grain，此调用会触发激活
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");

                // 查找激活 Span。
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"Activation Span found: {activationSpan is not null}");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"Activation Span ParentSpanId: {activationSpan.ParentSpanId}");
                    _output.WriteLine($"Activation Span ParentId: {activationSpan.ParentId}");
                }

                // 关键断言：必须存在激活跟踪跨度
                Assert.NotNull(activationSpan);

                // 关键断言：激活跟踪跨度必须与客户端共享同一 TraceId
                // 如果此断言失败，说明激活开启了新的跟踪，而不是
                // 作为入站 Grain 调用跟踪的一部分！
                Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());

                // 关键断言：激活跟踪跨度应具有父级（而不是根跟踪跨度）
                // 在缺陷场景中，激活跟踪跨度没有父级，并启动新的跟踪
                Assert.False(
                    string.IsNullOrEmpty(activationSpan.ParentId),
                    "Activation span should have a parent! If this fails, the activation is starting a new trace.");

                // 验证 Grain 调用跟踪跨度也共享相同的跟踪 ID
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证由 Grain 调用触发激活时，整个跟踪层级结构正确：
        /// 1. 客户端出站跟踪跨度
        /// 2. 服务器入站 Grain 调用跟踪跨度（以客户端出站跨度为父级）
        /// 3. 激活跟踪跨度（应与 Grain 调用共享跟踪上下文）
        /// 4. OnActivateAsync 跟踪跨度（以激活跟踪跨度为父级）
        /// 
        /// 此测试检查完整的层级结构，确保没有跟踪跨度断开关联。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task FullTraceHierarchyIsCorrectWhenActivationTriggeredByGrainCall()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-hierarchy-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();
            var clientSpanId = clientActivity.SpanId.ToString();

            _output.WriteLine($"=== TEST START ===");
            _output.WriteLine($"Client Activity TraceId: {clientTraceId}");
            _output.WriteLine($"Client Activity SpanId: {clientSpanId}");

            try
            {
                // 使用具有 OnActivateAsync 的 Grain 类型，确保获得所有跟踪跨度
                var grain = _fixture.GrainFactory.GetGrain<IFilteredActivityGrain>(Random.Shared.Next());
                _ = await grain.GetActivityId();

                _output.WriteLine($"\n=== SPAN ANALYSIS ===");

                // 1. 查找客户端出站跟踪跨度
                var clientOutgoingSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Client
                                        && a.OperationName.Contains("GetActivityId"));

                _output.WriteLine($"\n1. Client Outgoing Span:");
                if (clientOutgoingSpan is not null)
                {
                    _output.WriteLine($"   TraceId: {clientOutgoingSpan.TraceId}");
                    _output.WriteLine($"   SpanId: {clientOutgoingSpan.SpanId}");
                    _output.WriteLine($"   ParentSpanId: {clientOutgoingSpan.ParentSpanId}");
                    _output.WriteLine($"   ParentId: {clientOutgoingSpan.ParentId}");
                }
                else
                {
                    _output.WriteLine($"   NOT FOUND!");
                }

                // 2. 查找服务器入站跟踪跨度
                var serverIncomingSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Server
                                        && a.OperationName.Contains("GetActivityId"));

                _output.WriteLine($"\n2. Server Incoming Span:");
                if (serverIncomingSpan is not null)
                {
                    _output.WriteLine($"   TraceId: {serverIncomingSpan.TraceId}");
                    _output.WriteLine($"   SpanId: {serverIncomingSpan.SpanId}");
                    _output.WriteLine($"   ParentSpanId: {serverIncomingSpan.ParentSpanId}");
                    _output.WriteLine($"   ParentId: {serverIncomingSpan.ParentId}");
                }
                else
                {
                    _output.WriteLine($"   NOT FOUND!");
                }

                // 3. 查找激活跟踪跨度
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"\n3. Activation Span:");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"   TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"   SpanId: {activationSpan.SpanId}");
                    _output.WriteLine($"   ParentSpanId: {activationSpan.ParentSpanId}");
                    _output.WriteLine($"   ParentId: {activationSpan.ParentId ?? "(null - ROOT SPAN!)"}");
                }
                else
                {
                    _output.WriteLine($"   NOT FOUND!");
                }

                // 4. 查找 OnActivateAsync 跟踪跨度
                var onActivateSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.OnActivate);

                _output.WriteLine($"\n4. OnActivateAsync Span:");
                if (onActivateSpan is not null)
                {
                    _output.WriteLine($"   TraceId: {onActivateSpan.TraceId}");
                    _output.WriteLine($"   SpanId: {onActivateSpan.SpanId}");
                    _output.WriteLine($"   ParentSpanId: {onActivateSpan.ParentSpanId}");
                    _output.WriteLine($"   ParentId: {onActivateSpan.ParentId}");
                }
                else
                {
                    _output.WriteLine($"   NOT FOUND (grain may not implement IGrainBase)");
                }

                // 断言
                _output.WriteLine($"\n=== ASSERTIONS ===");

                // 所有跟踪跨度应共享相同的 TraceId
                Assert.NotNull(clientOutgoingSpan);
                Assert.NotNull(serverIncomingSpan);
                Assert.NotNull(activationSpan);

                _output.WriteLine($"Checking all spans share TraceId {clientTraceId}...");

                Assert.Equal(clientTraceId, clientOutgoingSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Client outgoing span has correct TraceId");

                Assert.Equal(clientTraceId, serverIncomingSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Server incoming span has correct TraceId");

                // 这是关键检查：激活跟踪跨度必须共享跟踪 ID！
                Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Activation span has correct TraceId");

                // 客户端出站跟踪跨度应以测试 Activity 为父级
                Assert.Equal(clientSpanId, clientOutgoingSpan.ParentSpanId.ToString());
                _output.WriteLine($"  ✓ Client outgoing span is parented to test activity");

                // 服务器入站跟踪跨度应以客户端出站跟踪跨度为父级
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverIncomingSpan.ParentSpanId.ToString());
                _output.WriteLine($"  ✓ Server incoming span is parented to client outgoing span");

                // 激活跟踪跨度应具有父级（而不是根跟踪跨度）
                Assert.False(
                    string.IsNullOrEmpty(activationSpan.ParentId),
                    "Activation span should not be a root span! This indicates broken trace context propagation.");
                _output.WriteLine($"  ✓ Activation span has a parent (not a root span)");

                if (onActivateSpan is not null)
                {
                    Assert.Equal(clientTraceId, onActivateSpan.TraceId.ToString());
                    _output.WriteLine($"  ✓ OnActivateAsync span has correct TraceId");

                    Assert.Equal(activationSpan.SpanId.ToString(), onActivateSpan.ParentSpanId.ToString());
                    _output.WriteLine($"  ✓ OnActivateAsync span is parented to activation span");
                }

                _output.WriteLine($"\n=== ALL CHECKS PASSED ===");
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 诊断测试，检查消息到达服务器时，traceparent 是否正确存在于消息的
        /// RequestContextData 中。这有助于诊断跟踪上下文可能
        /// 未能传播的生产环境问题。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task DiagnoseTraceContextInRequestContextData()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-diagnostic-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine($"=== DIAGNOSTIC TEST ===");
            _output.WriteLine($"Client Activity TraceId: {clientTraceId}");
            _output.WriteLine($"Client Activity SpanId: {clientActivity.SpanId}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Server-side RequestContext Analysis ===");
                _output.WriteLine($"TraceParent from RequestContext: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL or MISSING!)"}");
                _output.WriteLine($"Server Activity TraceId: {serverTraceInfo.TraceId ?? "(NULL!)"}");
                _output.WriteLine($"Server Activity ParentSpanId: {serverTraceInfo.ParentSpanId ?? "(NULL!)"}");
                _output.WriteLine($"Server Activity HasRemoteParent: {serverTraceInfo.IsRemote}");

                // 诊断断言
                if (string.IsNullOrEmpty(serverTraceInfo.TraceParentFromRequestContext))
                {
                    _output.WriteLine("\n⚠️ WARNING: traceparent is NOT present in RequestContext!");
                    _output.WriteLine("This would cause activation spans to start a new trace.");
                    _output.WriteLine("Check that AddActivityPropagation() is called on both client and silo.");
                }
                else
                {
                    _output.WriteLine($"\n✓ traceparent IS present in RequestContext");

                    // 解析并验证 traceparent 格式
                    var parts = serverTraceInfo.TraceParentFromRequestContext.Split('-');
                    if (parts.Length >= 3)
                    {
                        var version = parts[0];
                        var traceId = parts[1];
                        var parentId = parts[2];

                        _output.WriteLine($"  Version: {version}");
                        _output.WriteLine($"  TraceId: {traceId}");
                        _output.WriteLine($"  ParentSpanId: {parentId}");

                        Assert.Equal(clientTraceId, traceId);
                        _output.WriteLine($"  ✓ TraceId matches client's TraceId");
                    }
                }

                // traceparent 应存在
                Assert.NotNull(serverTraceInfo.TraceParentFromRequestContext);
                Assert.NotEmpty(serverTraceInfo.TraceParentFromRequestContext);
                Assert.Contains(clientTraceId, serverTraceInfo.TraceParentFromRequestContext);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 关键测试：模拟调用代码没有活动的 Activity 的场景。
        /// 此时，ActivityPropagationOutgoingGrainCallFilter 的 StartActivity 将返回 null
        /// （因为没有父 Activity，且可能没有侦听器），也不会注入 traceparent。
        /// 
        /// 此测试验证在没有活动的 Activity 时发起 Grain 调用，
        /// 激活跟踪跨度不会有父级，并且会启动新的跟踪。
        /// 
        /// 此测试复现以下生产环境问题：
        /// - `activate grain` 跟踪跨度的 parentSpanId 为空
        /// - 跟踪看起来与发起调用的跟踪断开了关联
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationStartsNewTraceWhenCallerHasNoActivity()
        {
            Started.Clear();

            // 确保没有当前 Activity
            var previousActivity = Activity.Current;
            Activity.Current = null;

            try
            {
                _output.WriteLine("=== TEST: Calling grain with NO Activity.Current ===");
                _output.WriteLine($"Activity.Current before call: {Activity.Current?.Id ?? "(null)"}");

                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Server Response ===");
                _output.WriteLine($"Server TraceParent from RequestContext: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL - this is the bug!)"}");
                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");

                // 查找激活 Span。
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"\n=== Activation Span Analysis ===");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"Activation Span ParentId: {activationSpan.ParentId ?? "(NULL - ROOT SPAN!)"}");
                    _output.WriteLine($"Activation Span ParentSpanId: {activationSpan.ParentSpanId}");

                    // 没有调用方 Activity 时，激活跟踪跨度应无父级
                    // 这一场景与生产环境的跟踪相符
                    if (string.IsNullOrEmpty(activationSpan.ParentId))
                    {
                        _output.WriteLine("\n⚠️ EXPECTED BEHAVIOR: Activation span is a ROOT span (no parent)");
                        _output.WriteLine("This matches the production issue where activate grain has empty parentSpanId");
                    }
                }
                else
                {
                    _output.WriteLine("Activation span NOT FOUND");
                }

                // 记录预期行为：没有调用方 Activity 时，RequestContext 中不会包含 traceparent，
                // 而且激活将启动新的跟踪
                if (string.IsNullOrEmpty(serverTraceInfo.TraceParentFromRequestContext))
                {
                    _output.WriteLine("\n✓ CONFIRMED: traceparent is NOT in RequestContext when caller has no Activity");
                    _output.WriteLine("This causes the activation span to start a new trace (no parent)");
                }
            }
            finally
            {
                Activity.Current = previousActivity;
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 测试从另一个没有 Activity 上下文的 Grain 内部发起 Grain 调用时的行为。
        /// 此测试模拟以下场景：
        /// - Grain 定时器触发调用
        /// - 提醒器回调发起 Grain 调用
        /// - 流订阅处理程序
        /// - Grain 中的后台处理
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task GrainToGrainCallWithoutActivityContext()
        {
            Started.Clear();

            // 首先激活一个会发起嵌套调用的 Grain
            // 从一个 Activity 开始，确保第一个 Grain 在正确的上下文中激活
            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("setup-activity");
            clientActivity?.Start();

            var callerGrain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());

            // 首次调用以确保 Grain 已激活
            _ = await callerGrain.GetTraceContextInfo();

            clientActivity?.Stop();
            Activity.Current = null;
            Started.Clear();

            _output.WriteLine("=== TEST: Grain-to-grain call with no ambient activity ===");

            try
            {
                // 现在再发起一次调用，此时 Grain 已激活
                // 但如果此调用触发 Grain 内的嵌套调用，
                // 且没有 Activity.Current，则嵌套调用不会具有跟踪上下文
                var result = await callerGrain.GetNestedTraceContextInfo();

                _output.WriteLine($"Caller grain TraceId: {result.Local.TraceId}");
                _output.WriteLine($"Nested grain TraceId: {result.Nested.TraceId}");

                // 两者都应具有 Activity（即使没有 traceparent，服务器也会创建一个）
                Assert.True(result.Local.HasActivity);
                Assert.True(result.Nested.HasActivity);

                // 但两者应共享同一跟踪，因为 Grain 间调用会保留上下文
                Assert.Equal(result.Local.TraceId, result.Nested.TraceId);
            }
            finally
            {
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 关键测试：此测试复现了以下确切的生产环境问题：
        /// - OpenTelemetry 配置为侦听 Microsoft.Orleans.Lifecycle（用于激活跟踪跨度）
        /// - 但未侦听 Microsoft.Orleans.Application（用于 Grain 调用跟踪跨度）
        /// 
        /// 出现这种情况时：
        /// 1. 客户端发起 Grain 调用
        /// 2. ActivityPropagationOutgoingGrainCallFilter 尝试在 ApplicationGrainSource 上启动 Activity
        /// 3. StartActivity 返回 null，因为该来源没有侦听器
        /// 4. 不会将 traceparent 注入 RequestContext
        /// 5. 服务器收到不包含 traceparent 的消息
        /// 6. Catalog.GetOrCreateActivation 启动无父级的激活跟踪跨度
        /// 7. 激活跟踪跨度成为根跨度（与原始跟踪断开关联）
        /// 
        /// 生产环境中的预期跟踪（缺陷）：
        ///   activate grain（无父级，启动新的跟踪！）
        ///     ├── register directory entry（注册目录条目）
        ///     └── execute OnActivateAsync（执行 OnActivateAsync）
        /// 
        /// 此测试验证如果 Application 来源未被采样，
        /// 激活跟踪跨度将没有父级。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanHasNoParentWhenApplicationSourceNotSampled()
        {
            // 此测试记录仅对 Lifecycle 来源采样时的预期行为
            // 在测试夹具中确实对 Application 来源采样，因此此测试验证正确的行为
            // 生产环境中若未对 Application 来源采样，激活将成为根跟踪跨度

            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-sampling-test");
            clientActivity?.Start();

            _output.WriteLine("=== TEST: Verifying sampling behavior ===");
            _output.WriteLine($"Client Activity created: {clientActivity is not null}");
            _output.WriteLine($"Client Activity ID: {clientActivity?.Id}");

            if (clientActivity is null)
            {
                _output.WriteLine("\n⚠️ CLIENT ACTIVITY IS NULL!");
                _output.WriteLine("This means Microsoft.Orleans.Application source is NOT being sampled.");
                _output.WriteLine("The ActivityPropagationOutgoingGrainCallFilter will NOT inject traceparent.");
                _output.WriteLine("This causes activation spans to start new traces (no parent).");
            }

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Results ===");
                _output.WriteLine($"Server has traceparent in RequestContext: {!string.IsNullOrEmpty(serverTraceInfo.TraceParentFromRequestContext)}");
                _output.WriteLine($"Server TraceParent: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL)"}");

                // 按来源查找跟踪跨度
                var applicationSpans = Started.Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName).ToList();
                var lifecycleSpans = Started.Where(a => a.Source.Name == ActivitySources.LifecycleActivitySourceName).ToList();

                _output.WriteLine($"\n=== Spans by Source ===");
                _output.WriteLine($"Microsoft.Orleans.Application spans: {applicationSpans.Count}");
                foreach (var span in applicationSpans)
                {
                    _output.WriteLine($"  - {span.OperationName} (Kind: {span.Kind})");
                }
                _output.WriteLine($"Microsoft.Orleans.Lifecycle spans: {lifecycleSpans.Count}");
                foreach (var span in lifecycleSpans)
                {
                    _output.WriteLine($"  - {span.OperationName} (ParentId: {span.ParentId ?? "NULL - ROOT"})");
                }

                // 查找客户端出站跟踪跨度
                var clientOutgoingSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Client);

                _output.WriteLine($"\n=== Key Finding ===");
                if (clientOutgoingSpan is null && applicationSpans.Count == 0)
                {
                    _output.WriteLine("⚠️ No Application source spans found!");
                    _output.WriteLine("If this happens in production (no listener for Application source),");
                    _output.WriteLine("the activation span will have no parent.");
                }

                // 测试环境中对 Application 来源进行了采样，因此应存在跟踪跨度
                Assert.NotNull(clientActivity);
                Assert.True(applicationSpans.Count > 0,
                    "Application source spans should exist. In production, verify Microsoft.Orleans.Application is in your TracerProvider sources.");
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 诊断测试，检查是否对所有 Orleans ActivitySource 正确采样。
        /// 运行此测试以验证 OpenTelemetry 配置包含所有必要来源。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task DiagnoseActivitySourceSampling()
        {
            Started.Clear();

            _output.WriteLine("=== Checking ActivitySource Sampling ===\n");

            // 测试每个 Orleans ActivitySource
            var sourcesToTest = new[]
            {
                (ActivitySources.ApplicationGrainSource, "Microsoft.Orleans.Application"),
                (ActivitySources.RuntimeGrainSource, "Microsoft.Orleans.Runtime"),
                (ActivitySources.LifecycleGrainSource, "Microsoft.Orleans.Lifecycle"),
                (ActivitySources.StorageGrainSource, "Microsoft.Orleans.Storage"),
            };

            foreach (var (source, name) in sourcesToTest)
            {
                var testActivity = source.StartActivity($"test-{name}", ActivityKind.Internal);
                var isSampled = testActivity is not null;
                _output.WriteLine($"{name}: {(isSampled ? "✓ SAMPLED" : "✗ NOT SAMPLED")}");
                testActivity?.Stop();
            }

            _output.WriteLine("\n=== Implications ===");
            _output.WriteLine("For proper trace propagation, you need to sample:");
            _output.WriteLine("  - Microsoft.Orleans.Application (for grain call spans)");
            _output.WriteLine("  - Microsoft.Orleans.Lifecycle (for activation/deactivation spans)");
            _output.WriteLine("\nIf Application is NOT sampled but Lifecycle IS:");
            _output.WriteLine("  - Grain call spans won't be created");
            _output.WriteLine("  - traceparent won't be injected into messages");
            _output.WriteLine("  - Activation spans will start new traces (no parent)");

            // 实际发起一次 Grain 调用，以进行端到端验证
            _output.WriteLine("\n=== End-to-End Test ===");
            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("e2e-test");
            if (clientActivity is null)
            {
                _output.WriteLine("⚠️ Could not create client activity - Application source not sampled!");
            }
            else
            {
                clientActivity.Start();
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var info = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server received traceparent: {!string.IsNullOrEmpty(info.TraceParentFromRequestContext)}");

                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation span has parent: {!string.IsNullOrEmpty(activationSpan.ParentId)}");
                    if (string.IsNullOrEmpty(activationSpan.ParentId))
                    {
                        _output.WriteLine("⚠️ BUG: Activation span is a root span!");
                    }
                }

                clientActivity.Stop();
            }

            PrintActivityDiagnostics();
        }

        /// <summary>
        /// 关键测试：模拟来自 ASP.NET HTTP 请求处理程序的调用。
        /// 生产环境中的 Grain 调用往往源自 HTTP 终结点，其流程为：
        /// 1. ASP.NET 创建 HTTP Activity（例如 "GET /api/users/{id}"）
        /// 2. 控制器调用 Grain
        /// 3. Orleans 应将 HTTP 跟踪上下文传播至 Grain
        /// 
        /// 此测试验证存在 Activity.Current（来自 HTTP/ASP.NET）时，
        /// Orleans 会将其正确传播至 Grain 调用及激活跟踪跨度。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceContextPropagatedFromHttpActivityToGrainCall()
        {
            Started.Clear();

            // 模拟 ASP.NET HTTP Activity（其来源与 Orleans 不同）
            using var httpActivitySource = new ActivitySource("Microsoft.AspNetCore", "1.0.0");
            var httpListener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "Microsoft.AspNetCore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(httpListener);

            using var httpActivity = httpActivitySource.StartActivity("GET /api/users/{id}", ActivityKind.Server);
            httpActivity?.Start();

            Assert.NotNull(httpActivity);
            var httpTraceId = httpActivity.TraceId.ToString();

            _output.WriteLine("=== TEST: HTTP to Grain call trace propagation ===");
            _output.WriteLine($"HTTP Activity TraceId: {httpTraceId}");
            _output.WriteLine($"HTTP Activity SpanId: {httpActivity.SpanId}");
            _output.WriteLine($"Activity.Current: {Activity.Current?.Id}");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Server Response ===");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server TraceParent: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL)"}");
                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");

                // 查找激活 Span。
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"\n=== Activation Span ===");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"ParentId: {activationSpan.ParentId ?? "(NULL - ROOT!)"}");
                }

                // 服务器应具有与 HTTP Activity 相同的 TraceId
                Assert.Equal(httpTraceId, serverTraceInfo.TraceId);

                // 激活跟踪跨度也应共享相同的 TraceId
                if (activationSpan is not null)
                {
                    Assert.Equal(httpTraceId, activationSpan.TraceId.ToString());
                    Assert.False(string.IsNullOrEmpty(activationSpan.ParentId),
                        "Activation span should have a parent when called from HTTP activity");
                }
            }
            finally
            {
                httpActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 诊断测试：强制在特定的（不同的）Silo 上激活 Grain，以测试跨 Silo
        /// 跟踪上下文传播。此测试模拟生产场景：放置
        /// 决策器将 Grain 放置到与最初接收调用的 Silo 不同的 Silo 上。
        /// 
        /// 生产环境使用 AddDistributedGrainDirectory() 时：
        /// 1. 客户端向 Silo A 发送调用
        /// 2. 放置策略决定 Grain 应位于 Silo B
        /// 3. 消息被转发至 Silo B
        /// 4. Silo B 创建激活实例
        /// 
        /// 问题在于：此次转发是否保留了跟踪上下文？
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceContextPreservedWhenGrainPlacedOnDifferentSilo()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("cross-silo-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            _output.WriteLine("=== TEST: Cross-silo grain activation ===");
            _output.WriteLine($"Client TraceId: {clientTraceId}");

            // 获取 Silo 集合
            var silos = _fixture.HostedCluster.GetActiveSilos().ToList();
            _output.WriteLine($"Active silos: {silos.Count}");
            foreach (var silo in silos)
            {
                _output.WriteLine($"  - {silo.SiloAddress}");
            }

            if (silos.Count < 2)
            {
                _output.WriteLine("⚠️ Need at least 2 silos for this test");
                return;
            }

            try
            {
                // 使用放置提示，强制将 Grain 放置在特定的 Silo 上
                var targetSilo = silos[1].SiloAddress; // 使用第二个 Silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetSilo);
                _output.WriteLine($"Placement hint set to: {targetSilo}");

                // 此 Grain 使用 RandomPlacement，因此放置提示应有效
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Results ===");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server TraceParent: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL!)"}");
                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");

                // 查找激活 Span。
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                if (activationSpan is not null)
                {
                    _output.WriteLine($"\nActivation Span:");
                    _output.WriteLine($"  TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"  ParentId: {activationSpan.ParentId ?? "(NULL - ROOT!)"}");
                    _output.WriteLine($"  HasRemoteParent: {activationSpan.HasRemoteParent}");
                }

                // 关键：即使跨 Silo 放置，也应保留跟踪
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);

                if (activationSpan is not null)
                {
                    Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());
                    Assert.False(string.IsNullOrEmpty(activationSpan.ParentId),
                        "Activation span should have a parent even with cross-silo placement");
                }
            }
            finally
            {
                RequestContext.Clear();
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 诊断测试：测试 Grain 间调用跨越 Silo 边界时的跟踪上下文。
        /// 生产环境中常见此情况，即 Silo 1 上的 Grain A 调用 Silo 2 上的 Grain B。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceContextPreservedInCrossSiloGrainToGrainCall()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("cross-silo-g2g-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);
            var clientTraceId = clientActivity.TraceId.ToString();

            var silos = _fixture.HostedCluster.GetActiveSilos().ToList();
            if (silos.Count < 2)
            {
                _output.WriteLine("⚠️ Need at least 2 silos for this test");
                return;
            }

            _output.WriteLine("=== TEST: Cross-silo grain-to-grain call ===");
            _output.WriteLine($"Client TraceId: {clientTraceId}");

            try
            {
                // 将第一个 Grain 放置在 Silo 1 上
                RequestContext.Set(IPlacementDirector.PlacementHintKey, silos[0].SiloAddress);
                var grain1 = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                _ = await grain1.GetTraceContextInfo(); // 在 Silo 1 上激活
                RequestContext.Clear();

                // 现在发起嵌套调用：嵌套的 Grain 应被放置至 Silo 2
                RequestContext.Set(IPlacementDirector.PlacementHintKey, silos[1].SiloAddress);
                var (local, nested) = await grain1.GetNestedTraceContextInfo();
                RequestContext.Clear();

                _output.WriteLine($"\n=== Results ===");
                _output.WriteLine($"Local grain TraceId: {local.TraceId}");
                _output.WriteLine($"Nested grain TraceId: {nested.TraceId}");
                _output.WriteLine($"Nested grain HasRemoteParent: {nested.IsRemote}");

                // 两者应共享相同的跟踪 ID
                Assert.Equal(clientTraceId, local.TraceId);
                Assert.Equal(clientTraceId, nested.TraceId);
            }
            finally
            {
                RequestContext.Clear();
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        private void PrintActivityDiagnostics()
        {
            var activities = Started.ToList();
            if (activities.Count == 0)
            {
                _output.WriteLine("No activities captured.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=== CAPTURED ACTIVITIES ===");
            sb.AppendLine($"Total: {activities.Count}");
            sb.AppendLine();

            foreach (var activity in activities.OrderBy(a => a.StartTimeUtc))
            {
                sb.AppendLine($"[{activity.Source.Name}] {activity.OperationName}");
                sb.AppendLine($"  ID: {activity.Id}");
                sb.AppendLine($"  TraceId: {activity.TraceId}");
                sb.AppendLine($"  SpanId: {activity.SpanId}");
                sb.AppendLine($"  ParentSpanId: {activity.ParentSpanId}");
                sb.AppendLine($"  ParentId: {activity.ParentId}");
                sb.AppendLine($"  Kind: {activity.Kind}");
                sb.AppendLine($"  HasRemoteParent: {activity.HasRemoteParent}");
                sb.AppendLine();
            }

            _output.WriteLine(sb.ToString());
        }
    }


    /// <summary>
    /// 用于验证客户端至 Grain 的跟踪上下文传播的测试 Grain 接口。
    /// 返回详细的跟踪信息，以验证服务器收到了正确的跟踪上下文。
    /// </summary>
    public interface ITraceContextPropagationGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 返回服务器端 Activity.Current 中的详细跟踪信息。
        /// 这样测试就能验证跟踪上下文是否正确传播。
        /// </summary>
        Task<TraceContextInfo> GetTraceContextInfo();

        /// <summary>
        /// 调用另一个 Grain，并返回本地和嵌套的跟踪上下文。
        /// 用于验证 Grain 间调用的跟踪上下文传播。
        /// </summary>
        Task<(TraceContextInfo Local, TraceContextInfo Nested)> GetNestedTraceContextInfo();
    }

    /// <summary>
    /// Grain 调用返回的详细跟踪上下文信息。
    /// </summary>
    [GenerateSerializer]
    public class TraceContextInfo
    {
        [Id(0)]
        public string? ActivityId { get; set; }

        [Id(1)]
        public string? TraceId { get; set; }

        [Id(2)]
        public string? SpanId { get; set; }

        [Id(3)]
        public string? ParentSpanId { get; set; }

        [Id(4)]
        public string? ParentId { get; set; }

        [Id(5)]
        public string? OperationName { get; set; }

        [Id(6)]
        public string? TraceParentFromRequestContext { get; set; }

        [Id(7)]
        public bool HasActivity { get; set; }

        [Id(8)]
        public ActivityKind Kind { get; set; }

        [Id(9)]
        public bool IsRemote { get; set; }
    }

    /// <summary>
    /// 用于验证跟踪上下文传播的测试 Grain 实现。
    /// </summary>
    public class TraceContextPropagationGrain : Grain, ITraceContextPropagationGrain
    {
        public Task<TraceContextInfo> GetTraceContextInfo()
        {
            var activity = Activity.Current;
            var traceParent = RequestContext.Get("traceparent") as string;

            return Task.FromResult(new TraceContextInfo
            {
                HasActivity = activity is not null,
                ActivityId = activity?.Id,
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString(),
                ParentSpanId = activity?.ParentSpanId.ToString(),
                ParentId = activity?.ParentId,
                OperationName = activity?.OperationName,
                Kind = activity?.Kind ?? ActivityKind.Internal,
                IsRemote = activity?.HasRemoteParent ?? false,
                TraceParentFromRequestContext = traceParent
            });
        }

        public async Task<(TraceContextInfo Local, TraceContextInfo Nested)> GetNestedTraceContextInfo()
        {
            var localInfo = await GetTraceContextInfo();

            var nestedGrain = GrainFactory.GetGrain<ITraceContextPropagationGrain>(this.GetPrimaryKeyLong() + 1);
            var nestedInfo = await nestedGrain.GetTraceContextInfo();

            return (localInfo, nestedInfo);
        }
    }

}

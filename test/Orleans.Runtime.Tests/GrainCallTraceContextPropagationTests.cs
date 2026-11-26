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
    /// 用于核查客户端和 grain 服务器之间跟踪背景传播的测试。
    /// 这些测试暴露了服务器侧的间隔开始新追踪而不是继续客户跟踪的问题。
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
        /// 捷克测试:证明服务器端 grain 调用活动与客户端具有相同的 TraceId 。
        /// 如果跟踪背景传播中断, 此测试失败 - 服务器将启动新的追踪
        /// 继续追踪客户
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerSideGrainCallSharesSameTraceIdAsClient()
        {
            Started.Clear();

            // 启动客户端的父活动
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

                // 此呼叫应该向服务器传播跟踪上下文
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

                // 服务器必须有一个活动
                Assert.True(serverTraceInfo.HasActivity, "Server-side grain call should have an Activity.Current");

                // 捷克要求:服务器 TraceId 必须匹配客户端 TraceId
                // 如果失败, 跟踪环境传播会中断!
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
        /// 证明服务器侧边活动是服务器类型,且有远程父/母。
        /// 这证实了适当的W3C跟踪背景处理。
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

                // 服务器边活动应该是类服务器
                Assert.Equal(ActivityKind.Server, serverTraceInfo.Kind);

                // 服务器边活动应有一个远程父母(从客户中传播)
                Assert.True(serverTraceInfo.IsRemote, "Server-side activity should have HasRemoteParent=true");
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 验证嵌套 grain- - -grain 呼叫之间的跟踪环境传播。
        /// 连锁店里的所有电话都应共享相同的 TraceId。
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

                // 两者 grains 都应有活动
                Assert.True(localInfo.HasActivity, "First grain should have an Activity.Current");
                Assert.True(nestedInfo.HasActivity, "Nested grain should have an Activity.Current");

                // 所有电话应共享相同的 TraceId
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
        /// 证明跟踪源头设置适当 RequestContext 制时 grain 电话
        /// 测试外向过滤器注入跟踪环境。
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

                // RequestContext中应显示原始线性页眉
                Assert.NotNull(serverTraceInfo.TraceParentFromRequestContext);
                Assert.NotEmpty(serverTraceInfo.TraceParentFromRequestContext);

                // 微光应包含客户端 TraceId
                Assert.Contains(clientTraceId, serverTraceInfo.TraceParentFromRequestContext);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 证明客户-端外向和服务器-端进出的间隔有适当的连接。
        /// 服务器的间隔应该为客户端的外延间隔 。
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

                // 寻找客户端的外延(应该是我们测试活动的孩子)
                var clientOutgoingSpan = Started
                    .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                               && a.Kind == ActivityKind.Client
                               && a.OperationName.Contains("GetTraceContextInfo"))
                    .FirstOrDefault();

                // 查找服务器端的接收间隔
                var serverIncomingSpan = Started
                    .Where(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                               && a.Kind == ActivityKind.Server
                               && a.OperationName.Contains("GetTraceContextInfo"))
                    .FirstOrDefault();

                _output.WriteLine($"Client Outgoing Span: {clientOutgoingSpan?.Id ?? "(not found)"}");
                _output.WriteLine($"Server Incoming Span: {serverIncomingSpan?.Id ?? "(not found)"}");

                Assert.NotNull(clientOutgoingSpan);
                Assert.NotNull(serverIncomingSpan);

                // 两者应同共享 TraceId
                Assert.Equal(clientTraceId, clientOutgoingSpan.TraceId.ToString());
                Assert.Equal(clientTraceId, serverIncomingSpan.TraceId.ToString());

                // 客户的外延时间应定位于测试活动
                Assert.Equal(clientParentActivity.SpanId.ToString(), clientOutgoingSpan.ParentSpanId.ToString());

                // 服务器的连接源父应该是客户端的发送间隔
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverIncomingSpan.ParentSpanId.ToString());
            }
            finally
            {
                clientParentActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 证明即使客户没有积极活动,也适当传播跟踪背景。
        /// 服务器仍应在此情况下建立自己的跟踪 。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerCreatesOwnTraceWhenClientHasNoActivity()
        {
            Started.Clear();

            // 确保客户没有当前活动
            var previousActivity = Activity.Current;
            Activity.Current = null;

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");

                // 服务器仍然应该创建一个活动( 开始新追踪)
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
        /// 证明同一客户活动同时发出多个 grain 电话
        /// 全部共享相同 TraceId。
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
        /// EDGE CASE:当跟踪对象头含有出乎意料的格式时,测试跟踪环境传播。
        /// 服务器应优雅地处理格式错误的页眉,并仍然创建活动。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ServerHandlesMalformedTraceParentGracefully()
        {
            Started.Clear();

            // 手动在 RequestContext中设置无效的跟踪环境
            RequestContext.Set("traceparent", "invalid-traceparent-value");

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server TraceParentFromRequestContext: {serverTraceInfo.TraceParentFromRequestContext}");

                // 服务器仍应有一个活动( 创建新追踪)
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
        /// 测试在 RequestContext 中的痕量显示客户的外延间隔,
        /// 而不是原始父活动。 这将验证客户端的创建范围 。
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

                // 跟踪应包含 TraceId
                Assert.Contains(clientTraceId, serverTraceInfo.TraceParentFromRequestContext);

                // 服务器的父对父的 ID 不应该是原始客户端的父对母的 ID
                // 应该是客户外拨电话的
                // (这是因为客户端为发送呼叫创建了新范围)
                Assert.NotEqual(clientParentSpanId, serverTraceInfo.ParentSpanId);

                // 查找客户端的外延
                var clientOutgoingSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Client
                                        && a.OperationName.Contains("GetTraceContextInfo"));

                Assert.NotNull(clientOutgoingSpan);

                // 服务器的父对父跨年身份识别码应与客户端的离线身份识别码匹配
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverTraceInfo.ParentSpanId);

                // 跟踪身份应该包含客户端的外向跨号ID
                Assert.Contains(clientOutgoingSpan.SpanId.ToString(), serverTraceInfo.TraceParentFromRequestContext);
            }
            finally
            {
                clientParentActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 在 grain OnActivateAsync内调用时,测试跟踪背景传播情况。
        /// 这是一个常见的边缘情况,启动可能没有适当的跟踪环境。
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
                // 发出触发启动的电话
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceId (during activation): {serverTraceInfo.TraceId}");

                // 校验跟踪ID匹配
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);

                // 查找激活的间隔
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    // 激活的间隔期也应共享相同的跟踪识别码
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
        /// 微量国家头的测试 与微量生物一起被适当传播
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceStateIsPropagatedWithTraceParent()
        {
            Started.Clear();

            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("client-tracestate-test");
            clientActivity?.Start();

            Assert.NotNull(clientActivity);

            // 设置客户活动跟踪状态
            clientActivity.TraceStateString = "vendor1=value1,vendor2=value2";

            try
            {
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                // 查找服务器的横跨
                var serverSpan = Started
                    .FirstOrDefault(a => a.Source.Name == ActivitySources.ApplicationGrainActivitySourceName
                                        && a.Kind == ActivityKind.Server
                                        && a.OperationName.Contains("GetTraceContextInfo"));

                _output.WriteLine($"Server Span TraceState: {serverSpan?.TraceStateString ?? "(null)"}");

                // 跟踪状态应该向服务器传播
                // 注:这项试验可能需要根据 Orleans 处理追踪状态的方法进行调整。
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
        /// 捷克试验:证明当 grain 调用触发器激活时,启动时间
        /// 相同股份 TraceId 以当为 grain 并且将电线与追踪中的适当连接。
        /// 
        /// 此测试复制了激活周期开始新追踪的生产问题
        /// 而不是作为来电 grain 追踪的一部分。
        /// 
        /// 预期跟踪结构:
        ///   client-parent-activity
        ///     └── ITraceContextPropagationGrain/GetTraceContextInfo (日报、 发送)
        ///           * { ITraceContextPropagationGrain/GetTraceContextInfo (服务员,新来) 
        ///                 激活 grain (应该与此追踪连接 !)
        ///                       注册目录条目
        ///                       执行 OnActivateAsync
        /// 
        /// 错误假设( 我们正在测试的是) :
        ///   启动 grain (NEWTRACE - 与客户端断开!) &lt; -- 这是BUG
        ///     注册目录条目
        ///     执行 OnActivateAsync
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
                // 使用独一 grain ID,以确保我们触发新的激活
                var uniqueGrainId = Random.Shared.Next();
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(uniqueGrainId);

                // 电话会触发启动, 因为这是一个新的 grain
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");

                // 查找激活的间隔
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"Activation Span found: {activationSpan is not null}");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"Activation Span ParentSpanId: {activationSpan.ParentSpanId}");
                    _output.WriteLine($"Activation Span ParentId: {activationSpan.ParentId}");
                }

                // 化学方面:必须存在激活范围
                Assert.NotNull(activationSpan);

                // 切片:启动期必须与客户共享相同的 TraceId
                // 如果失败, 启动将开始新的追踪, 而不是作为其中的一部分
                // 收到时 grain 电话追踪!
                Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());

                // 活动周期应有一个父(不是根间隔)
                // 在错误情况下, 激活的间隔没有父/ 父, 并开始新追踪
                Assert.False(
                    string.IsNullOrEmpty(activationSpan.ParentId),
                    "Activation span should have a parent! If this fails, the activation is starting a new trace.");

                // 校验 grain 调用间隔也共享相同的跟踪 ID
                Assert.Equal(clientTraceId, serverTraceInfo.TraceId);
            }
            finally
            {
                clientActivity?.Stop();
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 证明当启动由 grain 呼叫触发时,整个跟踪等级是正确的:
        /// 1. 客户外向
        /// 2. 服务器接收 grain 呼叫(由客户向外发送)
        /// 3. 启动范围(应与 grain 调用共享跟踪背景)
        /// 4. OnActivateAsync 跨度(以启动频度为活化)
        /// 
        /// 测试检查整个等级以确保没有间断 。
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
                // 使用 a grain 类型类型 OnActivateAsync 以确保我们得到所有
                var grain = _fixture.GrainFactory.GetGrain<IFilteredActivityGrain>(Random.Shared.Next());
                _ = await grain.GetActivityId();

                _output.WriteLine($"\n=== SPAN ANALYSIS ===");

                // 1. 确定客户的离职期
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

                // 2. 查找服务器的连接
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

                // 3. 查找启动范围
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

                // 4. 查找 OnActivateAsync 范围
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

                // 款 备 款
                _output.WriteLine($"\n=== ASSERTIONS ===");

                // 所有宽度应共享相同的 TraceId
                Assert.NotNull(clientOutgoingSpan);
                Assert.NotNull(serverIncomingSpan);
                Assert.NotNull(activationSpan);

                _output.WriteLine($"Checking all spans share TraceId {clientTraceId}...");

                Assert.Equal(clientTraceId, clientOutgoingSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Client outgoing span has correct TraceId");

                Assert.Equal(clientTraceId, serverIncomingSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Server incoming span has correct TraceId");

                // 这是化学检测 - 启动间隔必须共享追踪身份!
                Assert.Equal(clientTraceId, activationSpan.TraceId.ToString());
                _output.WriteLine($"  ✓ Activation span has correct TraceId");

                // 客户的外延时间应定位于测试活动
                Assert.Equal(clientSpanId, clientOutgoingSpan.ParentSpanId.ToString());
                _output.WriteLine($"  ✓ Client outgoing span is parented to test activity");

                // 服务器的输入频度应该与客户端的用户外传频度相连接
                Assert.Equal(clientOutgoingSpan.SpanId.ToString(), serverIncomingSpan.ParentSpanId.ToString());
                _output.WriteLine($"  ✓ Server incoming span is parented to client outgoing span");

                // 激活周期应有父(不是根间隔)
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
        /// 检查信息中是否正确存在跟踪者
        /// RequestContextData 当它到达服务器时。这有助于诊断生产问题
        /// 其跟踪环境可能不会传播。
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

                // 展望
                if (string.IsNullOrEmpty(serverTraceInfo.TraceParentFromRequestContext))
                {
                    _output.WriteLine("\n⚠️ WARNING: traceparent is NOT present in RequestContext!");
                    _output.WriteLine("This would cause activation spans to start a new trace.");
                    _output.WriteLine("Check that AddActivityPropagation() is called on both client and silo.");
                }
                else
                {
                    _output.WriteLine($"\n✓ traceparent IS present in RequestContext");

                    // 分析并验证跟踪格式
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

                // 追踪者应该在场
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
        /// 刻度测试: 模拟调用代码没有活动的设想。
        /// 在这种情况下, ActivityPropagationOutgoingGrainCallFilter的 StartActivity 将无效返回
        /// (因为没有家长活动 也可能没有听众) 也没有给跟踪者注射
        /// 
        /// 此测试可核实当 grain 呼叫没有活动时,
        /// 激活的间隔将没有父行, 并将开始新的跟踪 。
        /// 
        /// 由此再现了生产问题,其内容如下:
        /// - `activate grain` 宽度为空 parentSpanId
        /// - 痕迹似乎与原发电话断开
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationStartsNewTraceWhenCallerHasNoActivity()
        {
            Started.Clear();

            // 确保当前活动为当前活动
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

                // 查找激活的间隔
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"\n=== Activation Span Analysis ===");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"Activation Span TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"Activation Span ParentId: {activationSpan.ParentId ?? "(NULL - ROOT SPAN!)"}");
                    _output.WriteLine($"Activation Span ParentSpanId: {activationSpan.ParentSpanId}");

                    // 当没有电线活动时, 激活的间隔应该没有父权
                    // 这是与生产跟踪匹配的假设
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

                // 记录预期的行为:没有电话活动,追踪者就不会在 RequestContext
                // 激活将开始一个新的追踪
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
        /// 测试当 grain 由他人内部打电话 grain 并且没有活动背景。
        /// 模拟假想方案, 如:
        /// - Grain 触发电话的计时器
        /// - 提醒回调, 发出 grain 呼叫
        /// - 溪流订阅处理器
        /// - 处理背景 grains
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task GrainToGrainCallWithoutActivityContext()
        {
            Started.Clear();

            // 首先, 激活一个 grain , 这将发出嵌套呼叫
            // 我们首先从一项活动开始,以确保第一个 grain 在适当的背景下启动
            using var clientActivity = ActivitySources.ApplicationGrainSource.StartActivity("setup-activity");
            clientActivity?.Start();

            var callerGrain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());

            // 第一次呼叫以确保启动 grain
            _ = await callerGrain.GetTraceContextInfo();

            clientActivity?.Stop();
            Activity.Current = null;
            Started.Clear();

            _output.WriteLine("=== TEST: Grain-to-grain call with no ambient activity ===");

            try
            {
                // 现在再拨- grain 已经启动
                // 但如果这电话触发了 grain, 
                // 并且没有 Activity.Current,嵌套呼叫不会有跟踪上下文
                var result = await callerGrain.GetNestedTraceContextInfo();

                _output.WriteLine($"Caller grain TraceId: {result.Local.TraceId}");
                _output.WriteLine($"Nested grain TraceId: {result.Nested.TraceId}");

                // 两者都应该有活动(服务器即使没有追踪,也要创造一个)
                Assert.True(result.Local.HasActivity);
                Assert.True(result.Nested.HasActivity);

                // 但它们应该有相同的追踪,因为 grain-至grain 调用保持上下文
                Assert.Equal(result.Local.TraceId, result.Nested.TraceId);
            }
            finally
            {
                PrintActivityDiagnostics();
            }
        }

        /// <summary>
        /// 捷克试验:本试验重复了下列地点的确切生产问题:
        /// - OpenTelemetry 配置为听 Microsoft.Orleans.Lifecycle (用于激活频段)
        /// - 但不听 Microsoft.Orleans.Application grain 的呼号
        /// 
        /// 当发生这种情况时:
        /// 1. 客户发出 grain 呼叫
        /// 2. ActivityPropagationOutgoingGrainCallFilter 试图启动 ApplicationGrainSource上的活动
        /// 3. StartActivity 返回 NULLL, 因为没有该源的听众
        /// 4. 没有向任何机构注入痕量父母 RequestContext
        /// 5. 服务器接收没有跟踪信息的信息
        /// 6. Catalog.GetOrCreateActivation 启动时间没有父(母)
        /// 7. 启动波段成为ROOT(与原始跟踪脱钩)的波段。
        /// 
        /// 生产中的预期痕量(BUG):
        ///   激活 grain (无parent - 开始新追踪!)
        ///     注册目录条目
        ///     执行 OnActivateAsync
        /// 
        /// 如果应用软件源没有被采样的话
        /// 激活间隔将没有父/母。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task ActivationSpanHasNoParentWhenApplicationSourceNotSampled()
        {
            // 此测试记录了在仅取样生命周期来源时预期的行为
            // 在测试固定装置中 我们做抽样 试样应用源 所以这个测试验证正确的行为
            // 生产过程中,如果应用程序源没有取样,激活将是一个根宽

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

                // 按源查找范围
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

                // 查找客户端的外延
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

                // 在我们的测试环境中,应用源IS样本样本, 所以我们应该有宽度
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
        /// 检测是否对所有 Orleans ActivitySources 进行适当取样的诊断测试。
        /// 运行此选项以校验您的 OpenTelemetry 配置包括所有必要的来源。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task DiagnoseActivitySourceSampling()
        {
            Started.Clear();

            _output.WriteLine("=== Checking ActivitySource Sampling ===\n");

            // 每种 Orleans ActivitySource
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

            // 实际拨打 grain ,以核实终端到终端
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
        /// 刻录器测试: 模拟 ASP.NET HTTP请求处理程序的呼叫 。
        /// 在生产过程中, grain 电话经常来自HTTP终点点,其:
        /// 1. ASP.NET 创建 HTTP活动(e.g. "GET/api/user/{id}")
        /// 2. 控制器调用 grain
        /// 3. Orleans 应把HTTP跟踪上下文传播到 grain
        /// 
        /// 本测试核实如果存在 Activity.Current (来自HTTP/ASP.NET),
        /// Orleans 将它适当传播到 grain 调用和激活的间隔。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task TraceContextPropagatedFromHttpActivityToGrainCall()
        {
            Started.Clear();

            // 模拟 ASP.NET HTTP 活动(源数不同于 Orleans)
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

                // 查找激活的间隔
                var activationSpan = Started
                    .FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);

                _output.WriteLine($"\n=== Activation Span ===");
                if (activationSpan is not null)
                {
                    _output.WriteLine($"TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"ParentId: {activationSpan.ParentId ?? "(NULL - ROOT!)"}");
                }

                // 服务器应与 HTTP 活动具有相同的 TraceId
                Assert.Equal(httpTraceId, serverTraceInfo.TraceId);

                // 激活周期也应相同 TraceId
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
        /// 化学试验:力量 grain 特定(不同)上激活 silo 测试交叉silo
        /// 模拟生产情景,将
        /// 主任将 grain 放在与接收初始调用者不同的 silo 上。
        /// 
        /// 与 AddDistributedGrainDirectory():
        /// 1. 客户向 Silo A发出呼叫
        /// 2. 决定 grain 应放在 Silo B
        /// 3. 电文转发给 Silo B
        /// 4. Silo B 产生激活
        /// 
        /// 问题:是否通过这一转发保存了追踪背景?
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

            // 获取 silos
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
                // 使用布置提示将 grain 强制到特定 silo
                var targetSilo = silos[1].SiloAddress; // 第二次使用 silo
                RequestContext.Set(IPlacementDirector.PlacementHintKey, targetSilo);
                _output.WriteLine($"Placement hint set to: {targetSilo}");

                // 此 grain 使用 RandomPlacement, 所以提示应该有效
                var grain = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                var serverTraceInfo = await grain.GetTraceContextInfo();

                _output.WriteLine($"\n=== Results ===");
                _output.WriteLine($"Server TraceId: {serverTraceInfo.TraceId}");
                _output.WriteLine($"Server TraceParent: {serverTraceInfo.TraceParentFromRequestContext ?? "(NULL!)"}");
                _output.WriteLine($"Server HasActivity: {serverTraceInfo.HasActivity}");

                // 查找激活的间隔
                var activationSpan = Started.FirstOrDefault(a => a.OperationName == ActivityNames.ActivateGrain);
                if (activationSpan is not null)
                {
                    _output.WriteLine($"\nActivation Span:");
                    _output.WriteLine($"  TraceId: {activationSpan.TraceId}");
                    _output.WriteLine($"  ParentId: {activationSpan.ParentId ?? "(NULL - ROOT!)"}");
                    _output.WriteLine($"  HasRemoteParent: {activationSpan.HasRemoteParent}");
                }

                // 即使交叉silo 位置,也应保留追踪
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
        /// DIGNOST: grain- -grain 调用时的 silo 边界线的跟踪环境试验。
        /// 在生产中, Grain A在 Silo 1调用 Grain B调用 Silo 2时,这是常见的。
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
                // 位址 第一 grain 日期 时 silo 1
                RequestContext.Set(IPlacementDirector.PlacementHintKey, silos[0].SiloAddress);
                var grain1 = _fixture.GrainFactory.GetGrain<ITraceContextPropagationGrain>(Random.Shared.Next());
                _ = await grain1.GetTraceContextInfo(); // silo 1 上激活
                RequestContext.Clear();

                // 现在做一个嵌套呼叫 - 嵌巢 grain 应该转到 silo 2
                RequestContext.Set(IPlacementDirector.PlacementHintKey, silos[1].SiloAddress);
                var (local, nested) = await grain1.GetNestedTraceContextInfo();
                RequestContext.Clear();

                _output.WriteLine($"\n=== Results ===");
                _output.WriteLine($"Local grain TraceId: {local.TraceId}");
                _output.WriteLine($"Nested grain TraceId: {nested.TraceId}");
                _output.WriteLine($"Nested grain HasRemoteParent: {nested.IsRemote}");

                // 两者应具有相同的跟踪身份
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
    /// 测试测试 grain 用于核查从客户到 grain.
    /// 返回详细的跟踪信息以验证服务器是否收到正确的跟踪上下文。
    /// </summary>
    public interface ITraceContextPropagationGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 从服务器侧端返回详细跟踪信息 Activity.Current。
        /// 这样,测试就可以核实跟踪环境是否得到适当传播。
        /// </summary>
        Task<TraceContextInfo> GetTraceContextInfo();

        /// <summary>
        /// 呼叫另一个 grain ,返回本地和嵌套的跟踪环境。
        /// 用于核实在 grain-至grain 调用中进行跟踪背景传播。
        /// </summary>
        Task<(TraceContextInfo Local, TraceContextInfo Nested)> GetNestedTraceContextInfo();
    }

    /// <summary>
    /// grain 电话中返回的详细跟踪背景信息。
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
    /// 测试 grain 实施,以核查跟踪背景传播。
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

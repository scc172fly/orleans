using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orleans.Streams;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;
using Orleans.Providers;
using System.Diagnostics;

namespace UnitTests.General
{
    internal interface IMyRegularInterface
    {
        [Alias("Set")]
        ValueTask SetExtensionValue(int value);

        [Alias("Get")]
        ValueTask<int> GetExtensionValue();
    }

    internal interface IMyOtherInterface
    {
        [Alias("Set")]
        ValueTask SetExtensionValue(int value);

        [Alias("Get")]
        ValueTask<int> GetExtensionValue();
    }

    internal interface IMyGrainExtension : IGrainExtension, IMyRegularInterface, IMyOtherInterface
    {
    }

    internal sealed class MyGrainExtension : IMyGrainExtension
    {
        private int _value;

        ValueTask<int> IMyRegularInterface.GetExtensionValue() => new(_value);

        public ValueTask SetExtensionValue(int value)
        {
            _value = value;
            return default;
        }

        ValueTask<int> IMyOtherInterface.GetExtensionValue() => new(100 + _value);
    }

    /// <summary>
    /// Orleans grain 调用过滤器(拦截器)的全面测试。
    /// 
    /// Grain 呼叫过滤器提供AOP式拦截 grain 方法呼叫的方法,使:
    /// - 贯穿各领域的关切问题(博客、监测、安全)
    /// - 请求/答复操纵
    /// - 重试逻辑和错误处理
    /// - 方法代号计量和追踪
    /// 
    /// Orleans 支持两个进取过滤器(在目标 grain/silo上执行)和
    /// 退出过滤器( 在调用 grain/客户端时执行) 。 过滤器可以注册 :
    /// - 全系统(所有 grains)
    /// - 每一grain- 类型
    /// 
    /// 这些测试核查过滤执行令、环境传播、例外处理等。
    /// 与各种 Orleans 特征如流和观察员融合。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("BVT"), TestCategory("GrainCallFilter")]
    public class GrainCallFilterTests : OrleansTestingBase, IClassFixture<GrainCallFilterTests.Fixture>
    {
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
                builder.AddSiloBuilderConfigurator<SiloInvokerTestSiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<ClientConfigurator>();
            }

            private class SiloInvokerTestSiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder
                        .AddGrainExtension<IMyGrainExtension, MyGrainExtension>()
                        // 全系统进入过滤器 - 全部执行 grain 要求这样做 silo
                        .AddIncomingGrainCallFilter(context =>
                        {
                            Assert.NotNull(context);
                            Assert.NotNull(context.InterfaceMethod);
                            Assert.NotNull(context.Grain);
                            Assert.NotNull(context.ImplementationMethod);
                            Assert.NotNull(context.TargetContext);
                            Assert.NotEmpty(context.InterfaceName);
                            Assert.NotEmpty(context.MethodName);
                            Assert.False(context.TargetId.IsDefault);
                            Assert.False(context.InterfaceType.IsDefault);

                            // 测试1: 核查 RequestContext 通过过滤器的传播
                            // 每个过滤器加一个数字来构建“ 1234”
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrain.GetRequestContext), StringComparison.Ordinal))
                            {
                                if (RequestContext.Get(GrainCallFilterTestConstants.Key) != null) throw new InvalidOperationException();
                                RequestContext.Set(GrainCallFilterTestConstants.Key, "1");
                            }

                            // 测试2: 当过滤器不调用 context.Invoke() 时校验行为
                            // 这样做应导致 InvalidOperationException 拨打电话者
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrain.SystemWideCallFilterMarker), StringComparison.Ordinal))
                            {
                                // 明确不继续呼叫
                                return Task.CompletedTask;
                            }

                            // 测试3:示范请求操作操纵 - 否定值
                            // 此显示过滤器可以执行前修改方法参数
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IMyGrainExtension.SetExtensionValue), StringComparison.Ordinal))
                            {
                                context.Request.SetArgument(0, (int)context.Request.GetArgument(0)! * -1);
                            }

                            return context.Invoke();
                        })
                        .AddIncomingGrainCallFilter<GrainCallFilterWithDependencies>()
                        // 全系统外出过滤器 - 当此 silo 调用其它 grains时执行
                        .AddOutgoingGrainCallFilter(async ctx =>
                        {
                            // 将字符串参数翻一番修改已发送 Echo 调用
                            if (ctx.InterfaceMethod?.Name == "Echo")
                            {
                                // 将输入内容集中到自己身上。
                                var orig = (string)ctx.Request.GetArgument(0)!;
                                ctx.Request.SetArgument(0, orig + orig);
                            }

                            if (string.Equals(ctx.InterfaceMethod?.Name, nameof(IMethodInterceptionGrain.SystemWideCallFilterMarker), StringComparison.Ordinal))
                            {
                                // 明确不继续呼叫
                                return;
                            }

                            await ctx.Invoke();
                        })
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>("MemoryStreamProvider")
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("PubSubStore");
                }
            }

            private class ClientConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
                {
                    clientBuilder
                        .AddIncomingGrainCallFilter(context =>
                        {
                            Assert.NotNull(context);
                            Assert.NotNull(context.InterfaceMethod);
                            Assert.NotNull(context.Grain);
                            Assert.NotNull(context.ImplementationMethod);
                            Assert.NotNull(context.TargetContext);
                            Assert.NotEmpty(context.InterfaceName);
                            Assert.NotEmpty(context.MethodName);
                            Assert.False(context.TargetId.IsDefault);
                            Assert.False(context.InterfaceType.IsDefault);

                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrainObserver.GetRequestContext), StringComparison.Ordinal))
                            {
                                if (RequestContext.Get(GrainCallFilterTestConstants.Key) != null) throw new InvalidOperationException();
                                RequestContext.Set(GrainCallFilterTestConstants.Key, "1");
                            }

                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrainObserver.SystemWideCallFilterMarker), StringComparison.Ordinal))
                            {
                                // 明确不继续呼叫
                                return Task.CompletedTask;
                            }

                            return context.Invoke();
                        })
                        .AddIncomingGrainCallFilter<GrainCallFilterWithDependencies>()
                        .AddOutgoingGrainCallFilter(RetryCertainCalls)
                        .AddOutgoingGrainCallFilter(async context =>
                        {
                            Assert.NotNull(context);
                            Assert.NotNull(context.InterfaceMethod);
                            Assert.NotNull(context.Grain);
                            Assert.NotEmpty(context.InterfaceName);
                            Assert.NotEmpty(context.MethodName);
                            Assert.False(context.TargetId.IsDefault);
                            Assert.False(context.InterfaceType.IsDefault);
                            if (context.InterfaceMethod?.DeclaringType == typeof(IOutgoingMethodInterceptionGrain)
                                && context.InterfaceMethod?.Name == nameof(IOutgoingMethodInterceptionGrain.EchoViaOtherGrain))
                            {
                                context.Request.SetArgument(1, ((string)context.Request.GetArgument(1)!).ToUpperInvariant());
                            }

                            await context.Invoke();

                            if (context.InterfaceMethod?.DeclaringType == typeof(IOutgoingMethodInterceptionGrain)
                                && context.InterfaceMethod?.Name == nameof(IOutgoingMethodInterceptionGrain.EchoViaOtherGrain))
                            {
                                var result = (Dictionary<string, object>)context.Result!;
                                result["orig"] = result["result"];
                                result["result"] = "intercepted!";
                            }
                        })
                        .AddMemoryStreams<DefaultMemoryMessageBodySerializer>("MemoryStreamProvider");

                    // 演示在离出过滤器中重新引用逻辑
                    // 此过滤器重试修改参数, 无法调用
                    static async Task RetryCertainCalls(IOutgoingGrainCallContext ctx)
                    {
                        var attemptsRemaining = 2;

                        while (attemptsRemaining > 0)
                        {
                            try
                            {
                                await ctx.Invoke();
                                return;
                            }
                            catch (ArgumentOutOfRangeException) when (attemptsRemaining > 1 && ctx.Grain is IOutgoingMethodInterceptionGrain)
                            {
                                // 通过减少问题值重试
                                if (string.Equals(ctx.InterfaceMethod?.Name, nameof(IOutgoingMethodInterceptionGrain.ThrowIfGreaterThanZero), StringComparison.Ordinal) && ctx.Request.GetArgument(0) is int value)
                                {
                                    ctx.Request.SetArgument(0, value - 1);
                                }

                                --attemptsRemaining;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 显示 grain 呼叫过滤器可使用依赖注射。
        /// 此过滤器通过构建器注入接收 IGrainFactory ,
        /// 显示过滤器是数据交换容器的全部参与者。
        /// </summary>
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        public class GrainCallFilterWithDependencies(IGrainFactory grainFactory) : IIncomingGrainCallFilter
        {
            public Task Invoke(IIncomingGrainCallContext context)
            {
                Assert.NotNull(grainFactory);
                // 继续构建 RequestContext 值: “ 1” - >“ 12”
                if (string.Equals(context.ImplementationMethod?.Name, nameof(IGrainCallFilterTestGrain.GetRequestContext), StringComparison.Ordinal))
                {
                    if (RequestContext.Get(GrainCallFilterTestConstants.Key) is string value)
                    {
                        RequestContext.Set(GrainCallFilterTestConstants.Key, value + '2');
                    }
                }

                return context.Invoke();
            }
        }

        private readonly Fixture fixture;

        public GrainCallFilterTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 以请求/反应操作方式测试完整的离线过滤管道。
        /// 证明:
        /// 1. 客户外向过滤器将参数转换为大写:ab - > AB
        /// 2. 谷物1外向过滤器将字符串翻倍:AB - > ABAB
        /// 3. 谷物2进入过滤器逆向:ABAB - > BABA
        /// 4. 返回途中拦截和修改反应
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Outgoing_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IOutgoingMethodInterceptionGrain>(Random.Shared.Next());
            var grain2 = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 grain 方法读取上下文并返回它
            var result = await grain.EchoViaOtherGrain(grain2, "ab");

            // 原始的股本应该是:
            // 1. 离开客户时转换成大案: ab - > AB。
            // 2. 出口粮食1:AB - > ABAB, 双倍。
            // 3. 进入谷地的路上出现逆转:ABAB - > BABA。
            Assert.Equal("BABA", result["orig"] as string);
            Assert.NotNull(result["result"]);
            Assert.Equal("intercepted!", result["result"]);
        }

        /// <summary>
        /// 验证多个已收件 grain 呼叫过滤器的执行命令。
        /// RequestContext 的累计值应依以下顺序积累: “ 1” - > “ 12” - > “ 123” - > “ 1234”
        /// 每一位数由管道中不同的过滤器添加。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_Order_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 此 grain 方法读取上下文并返回它
            var context = await grain.GetRequestContext();
            Assert.NotNull(context);
            Assert.Equal("1234", context);
        }

        /// <summary>
        /// 测试 grain 调用过滤器是否被适当用于流式情景。
        /// 当一个 grain 接收流事件时, 输入的过滤器仍应执行,
        /// 允许流事件操作或监测。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_Stream_Test()
        {
            var streamProvider = this.fixture.Client.GetStreamProvider("MemoryStreamProvider");
            var id = Guid.NewGuid();
            var stream = streamProvider.GetStream<int>("InterceptedStream", id);
            var grain = this.fixture.GrainFactory.GetGrain<IStreamInterceptionGrain>(id);

            // 被截取的 grain 值应加倍于传递到流的值 。
            const int testValue = 43;
            await stream.OnNextAsync(testValue);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            int actual = 0;
            while (!cts.IsCancellationRequested)
            {
                actual = await grain.GetLastStreamValue();
                if (actual != 0) break;
            }

            Assert.Equal(testValue * 2, actual);
        }

        /// <summary>
        /// 演示在输入过滤器中重新引用逻辑 。
        /// 过滤器修改失败的参数值,以使调用成功。
        /// 显示过滤器如何可以实施复原力模式。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_Retry_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(0);

            var result = await grain.ThrowIfGreaterThanZero(1);
            Assert.Equal("Thanks for nothing", result);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => grain.ThrowIfGreaterThanZero(2));
        }

        /// <summary>
        /// 测试一个来电过滤器是否与 HashSet一起工作。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_HashSet_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(0);

            var result = await grain.SumSet(new HashSet<int> { 1, 2, 3 });
            Assert.Equal(6, result);
        }

        /// <summary>
        /// 测试一个即将退出的呼叫过滤器可以重新输入通话。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Outgoing_Retry_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IOutgoingMethodInterceptionGrain>(0);

            var result = await grain.ThrowIfGreaterThanZero(1);
            Assert.Equal("Thanks for nothing", result);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => grain.ThrowIfGreaterThanZero(2));
        }

        /// <summary>
        /// 测试 grain- 特定过滤器。
        /// 这些过滤器只对特定类型 grain 执行,而不是全系统范围。
        /// 演示:
        /// - 采用习惯逻辑的方法拦截
        /// - 选择性过滤(某些方法未拦截)
        /// - 获得实施方法信息
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GrainLevel_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(0);
            var result = await grain.One();
            Assert.Equal("intercepted one with no args", result);

            result = await grain.Echo("stao erom tae");
            Assert.Equal("eat more oats", result);// Grain 拦截器应接收执行的 MethodInfo ,而不是接口。

            result = await grain.NotIntercepted();
            Assert.Equal("not intercepted", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
        }

        /// <summary>
        /// 证明 grain 调用过滤器在通用 grain 类型下正确工作。
        /// 通用 grains 对基于反射的系统构成特殊挑战,
        /// 这样,过滤器就能确保能够按照通用 grain 方法正确截取呼叫。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GenericGrain_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGenericMethodInterceptionGrain<int>>(0);
            var result = await grain.GetInputAsString(679);
            Assert.Contains("Hah!", result);
            Assert.Contains("679", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
        }

        /// <summary>
        /// 测试 grains 上安装多个相同通用界面的测试过滤器。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_ConstructedGenericInheritance_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<ITrickyMethodInterceptionGrain>(0);

            var result = await grain.GetInputAsString("2014-12-19T14:32:50Z");
            Assert.Contains("Hah!", result);
            Assert.Contains("2014-12-19T14:32:50Z", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);

            var bestNumber = await grain.GetBestNumber();
            Assert.Equal(38, bestNumber);

            result = await grain.GetInputAsString(true);
            Assert.Contains(true.ToString(CultureInfo.InvariantCulture), result);
        }

        /// <summary>
        /// 演示 grain 调用过滤器中的例外处理。
        /// 过滤器可以从 grain 方法中获取例外, 并 :
        /// - 把它们变成不同的对策
        /// - 日志和重试
        /// - 转换为特定域的例外
        /// 这一测试显示,将例外转化为成功响应。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_ExceptionHandling_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 grain 方法投出, 但例外情况应该由过滤器之一处理并转换
            // 进入特定信息。
            var result = await grain.Throw();
            Assert.NotNull(result);
            Assert.Equal("EXCEPTION! Oi!", result);
        }

        /// <summary>
        /// 测试 grain 调用过滤器可丢弃例外 。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_FilterThrows_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            var exception = await Assert.ThrowsAsync<MethodInterceptionGrain.MyDomainSpecificException>(() => grain.FilterThrows());
            Assert.NotNull(exception);
            Assert.Equal("Filter THROW!", exception.Message);
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器为 <see cref="Orleans.IGrainCallContext.Result"/>设置错误的结果类型,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_SetIncorrectResultType_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 grain 方法投出, 但例外情况应该由过滤器之一处理并转换
            // 进入特定信息。
            await Assert.ThrowsAsync<InvalidCastException>(() => grain.IncorrectResultType());
        }

        /// <summary>
        /// 测试 grain 正确调用过滤器拦截呼叫到 grain 扩展。
        /// Grain 扩展是在现有 grains中添加额外接口的一种方式。
        /// 用于校验过滤器是否正确工作, 即使当方法被调用时
        /// 通过扩展界面而不是主 grain 接口。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_GrainExtension()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());
            var extension = grain.AsReference<IMyGrainExtension>();

            await ((IMyRegularInterface)extension).SetExtensionValue(42);
            var result = await ((IMyRegularInterface)extension).GetExtensionValue();
            Assert.Equal(-42, result);

            result = await ((IMyOtherInterface)extension).GetExtensionValue();
            Assert.Equal(100 - 42, result);
        }

        /// <summary>
        /// 当 grains 安装通用接口时,验证正确的方法分辨率。
        /// 测试复杂的继承情景,其中:
        /// - grain 实施多个通用界面
        /// - 不同界面使用相同名称的方法
        /// - 过滤器必须正确识别被称作哪个界面方法
        /// 这对于需要应用特定接口逻辑的过滤器至关重要。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GenericInterface_ConcreteGrain_Test()
        {
            var id = Random.Shared.Next();
            var hungry = this.fixture.GrainFactory.GetGrain<IHungryGrain<Apple>>(id);
            var caterpillar = this.fixture.GrainFactory.GetGrain<ICaterpillarGrain>(id);
            var omnivore = this.fixture.GrainFactory.GetGrain<IOmnivoreGrain>(id);

            RequestContext.Set("tag", "hungry-eat");
            await hungry.Eat(new Apple());
            await ((IHungryGrain<Apple>)caterpillar).Eat(new Apple());

            RequestContext.Set("tag", "omnivore-eat");
            await omnivore.Eat("string");
            await ((IOmnivoreGrain)caterpillar).Eat("string");

            RequestContext.Set("tag", "caterpillar-eat");
            await caterpillar.Eat("string");

            RequestContext.Set("tag", "hungry-eatwith");
            await caterpillar.EatWith(new Apple(), "butter");
            await hungry.EatWith(new Apple(), "butter");
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器不调用 <see cref="IGrainCallContext.Invoke"/>,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_SystemWideDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 电话过滤器不能继续使用因伏克语链, 但错误状态应该被抛出为
            // InvalidOperationException,不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器不调用 <see cref="IGrainCallContext.Invoke"/>,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GrainSpecificDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 电话过滤器不能继续使用因伏克语链, 但错误状态应该被抛出为
            // InvalidOperationException,不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GrainSpecificCallFilterMarker());
        }

        /// <summary>
        /// 测试如果即将退出 grain 的调用过滤器不调用 <see cref="IGrainCallContext.Invoke"/>,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Outgoing_SystemWideDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 电话过滤器不能继续使用因伏克语链, 但错误状态应该被抛出为
            // InvalidOperationException,不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
        }

        /// <summary>
        /// 确保以正确顺序在方法调用周围使用 grain 调用过滤器。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_Order_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 此 grain 方法读取上下文并返回它
            var context = await grain.GetRequestContext();
            Assert.NotNull(context);
            Assert.Equal("1234", context);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试一个输入的呼叫过滤器可以重新尝试调用到观察者。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_Retry_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            var result = await grain.ThrowIfGreaterThanZero(1);
            Assert.Equal("Thanks for nothing", result);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => grain.ThrowIfGreaterThanZero(2));
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试一个输入的呼叫过滤器对 HashSet观察者起作用。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_HashSet_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            var result = await grain.SumSet(new HashSet<int> { 1, 2, 3 });
            Assert.Equal(6, result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器不调用 <see cref="IGrainCallContext.Invoke"/>,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_SystemWideDoesNotCallContextInvoke_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 电话过滤器不能继续使用因伏克语链, 但错误状态应该被抛出为
            // InvalidOperationException,不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器不调用 <see cref="IGrainCallContext.Invoke"/>,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_GrainSpecificDoesNotCallContextInvoke_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 电话过滤器不能继续使用因伏克语链, 但错误状态应该被抛出为
            // InvalidOperationException,不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GrainSpecificCallFilterMarker());
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 仅在 grain 级别上测试过滤器。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_GrainLevel_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);
            var result = await grain.One();
            Assert.Equal("intercepted one with no args", result);

            result = await grain.Echo("stao erom tae");
            Assert.Equal("eat more oats", result);// Grain 拦截器应接收执行的 MethodInfo ,而不是接口。

            result = await grain.NotIntercepted();
            Assert.Equal("not intercepted", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 在通用 grains上测试过滤器。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_GenericGrain_Test()
        {
            var observer = new GenericMethodInterceptionGrainObserver<int>();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGenericMethodInterceptionGrainObserver<int>>(observer);
            var result = await grain.GetInputAsString(679);
            Assert.Contains("Hah!", result);
            Assert.Contains("679", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试 grains 上安装多个相同通用界面的测试过滤器。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_ConstructedGenericInheritance_Test()
        {
            var observer = new TrickyInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<ITrickyMethodInterceptionGrainObserver>(observer);

            var result = await grain.GetInputAsString("2014-12-19T14:32:50Z");
            Assert.Contains("Hah!", result);
            Assert.Contains("2014-12-19T14:32:50Z", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);

            var bestNumber = await grain.GetBestNumber();
            Assert.Equal(38, bestNumber);

            result = await grain.GetInputAsString(true);
            Assert.Contains(true.ToString(CultureInfo.InvariantCulture), result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试 grain 调用过滤器可处理例外。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_ExceptionHandling_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);

            // 此 grain 方法投出, 但例外情况应该由过滤器之一处理并转换
            // 进入特定信息。
            var result = await grain.Throw();
            Assert.NotNull(result);
            Assert.Equal("EXCEPTION! Oi!", result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试 grain 调用过滤器可丢弃例外 。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_FilterThrows_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);

            var exception = await Assert.ThrowsAsync<MethodInterceptionGrainObserver.MyDomainSpecificException>(() => grain.FilterThrows());
            Assert.NotNull(exception);
            Assert.Equal("Filter THROW!", exception.Message);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试如果 grain 调用过滤器为 <see cref="Orleans.IGrainCallContext.Result"/>设置错误的结果类型,
        /// 电话人被抛出例外。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_SetIncorrectResultType_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);

            // 此 grain 方法投出, 但例外情况应该由过滤器之一处理并转换
            // 进入特定信息。
            await Assert.ThrowsAsync<InvalidCastException>(() => grain.IncorrectResultType());
            GC.KeepAlive(observer);
        }
    }
}

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
    /// Orleans Grain 调用筛选器（拦截器）的全面测试。
    /// 
    /// Grain 调用筛选器以 AOP 风格拦截 Grain 方法调用，从而支持：
    /// - 横切关注点（日志记录、监控、安全性）
    /// - 请求/响应的修改
    /// - 重试逻辑与错误处理
    /// - 方法调用指标与跟踪
    /// 
    /// Orleans 支持入站筛选器（在目标 Grain/Silo 上执行），也支持
    /// 出站筛选器（在调用方 Grain/客户端上执行）。筛选器可以按以下范围注册：
    /// - 系统范围（所有 Grain）
    /// - 按 Grain 类型
    /// 
    /// 这些测试验证筛选器执行顺序、上下文传播、异常处理，
    /// 以及与流、观察者等 Orleans 功能的集成。
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
                        // 系统范围入站筛选器：为此 Silo 上的所有 Grain 调用执行
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

                            // 测试 1：验证 RequestContext 在筛选器间的传播
                            // 每个筛选器添加一位数字，最终组成 "1234"
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrain.GetRequestContext), StringComparison.Ordinal))
                            {
                                if (RequestContext.Get(GrainCallFilterTestConstants.Key) != null) throw new InvalidOperationException();
                                RequestContext.Set(GrainCallFilterTestConstants.Key, "1");
                            }

                            // 测试 2：验证筛选器不调用 context.Invoke() 时的行为
                            // 这应该导致调用方收到 InvalidOperationException
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IGrainCallFilterTestGrain.SystemWideCallFilterMarker), StringComparison.Ordinal))
                            {
                                // 显式停止继续调用 Invoke。
                                return Task.CompletedTask;
                            }

                            // 测试 3：演示请求修改，将数值取反
                            // 这说明筛选器可在执行方法之前修改方法参数
                            if (string.Equals(context.InterfaceMethod.Name, nameof(IMyGrainExtension.SetExtensionValue), StringComparison.Ordinal))
                            {
                                context.Request.SetArgument(0, (int)context.Request.GetArgument(0)! * -1);
                            }

                            return context.Invoke();
                        })
                        .AddIncomingGrainCallFilter<GrainCallFilterWithDependencies>()
                        // 系统范围出站筛选器：在此 Silo 调用其他 Grain 时执行
                        .AddOutgoingGrainCallFilter(async ctx =>
                        {
                            // 修改出站 Echo 调用，将字符串参数重复一遍
                            if (ctx.InterfaceMethod?.Name == "Echo")
                            {
                                // 将输入字符串与自身拼接。
                                var orig = (string)ctx.Request.GetArgument(0)!;
                                ctx.Request.SetArgument(0, orig + orig);
                            }

                            if (string.Equals(ctx.InterfaceMethod?.Name, nameof(IMethodInterceptionGrain.SystemWideCallFilterMarker), StringComparison.Ordinal))
                            {
                                // 显式停止继续调用 Invoke。
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
                                // 显式停止继续调用 Invoke。
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

                    // 演示出站筛选器中的重试逻辑
                    // 此筛选器通过修改参数来重试失败的调用
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
                                // 将导致问题的值递减后重试
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
        /// 演示 Grain 调用筛选器可以使用依赖注入。
        /// 此筛选器通过构造函数注入接收 IGrainFactory，
        /// 说明筛选器完全参与 DI 容器的工作。
        /// </summary>
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        public class GrainCallFilterWithDependencies(IGrainFactory grainFactory) : IIncomingGrainCallFilter
        {
            public Task Invoke(IIncomingGrainCallContext context)
            {
                Assert.NotNull(grainFactory);
                // 继续构造 RequestContext 的值："1" -> "12"
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
        /// 测试具有请求/响应修改功能的完整出站筛选器管道。
        /// 验证以下情况：
        /// 1. 客户端出站筛选器将参数转换为大写：ab -> AB
        /// 2. Grain1 出站筛选器将字符串重复一遍：AB -> ABAB
        /// 3. Grain2 入站筛选器将其反转：ABAB -> BABA
        /// 4. 响应在返回途中被拦截并修改
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Outgoing_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IOutgoingMethodInterceptionGrain>(Random.Shared.Next());
            var grain2 = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 Grain 方法读取上下文并返回它。
            var result = await grain.EchoViaOtherGrain(grain2, "ab");

            // 原始参数应经过以下处理：
            // 1. 从客户端发出时转换为大写：ab -> AB。
            // 2. 从 grain1 发出时重复一遍：AB -> ABAB。
            // 3. 进入 grain2 时反转：ABAB -> BABA。
            Assert.Equal("BABA", result["orig"] as string);
            Assert.NotNull(result["result"]);
            Assert.Equal("intercepted!", result["result"]);
        }

        /// <summary>
        /// 验证多个入站 Grain 调用筛选器的执行顺序。
        /// RequestContext 应按顺序累积值："1" -> "12" -> "123" -> "1234"
        /// 其中每位数字都由管道中的不同筛选器添加。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_Order_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 此 Grain 方法读取上下文并返回它。
            var context = await grain.GetRequestContext();
            Assert.NotNull(context);
            Assert.Equal("1234", context);
        }

        /// <summary>
        /// 测试流式处理场景中 Grain 调用筛选器能否正确执行。
        /// 当 Grain 收到流事件时，入站筛选器仍应执行，
        /// 使流事件可被修改或监控。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_Stream_Test()
        {
            var streamProvider = this.fixture.Client.GetStreamProvider("MemoryStreamProvider");
            var id = Guid.NewGuid();
            var stream = streamProvider.GetStream<int>("InterceptedStream", id);
            var grain = this.fixture.GrainFactory.GetGrain<IStreamInterceptionGrain>(id);

            // 被拦截的 Grain 应将传入流的值乘以二。
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
        /// 演示入站筛选器中的重试逻辑。
        /// 筛选器修改导致调用失败的参数值，使调用成功。
        /// 说明如何通过筛选器实现弹性模式。
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
        /// 测试传入调用筛选器与 HashSet 配合使用的行为。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_HashSet_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(0);

            var result = await grain.SumSet(new HashSet<int> { 1, 2, 3 });
            Assert.Equal(6, result);
        }

        /// <summary>
        /// 测试出站调用筛选器能够重试调用。
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
        /// 测试特定于 Grain 的筛选器。
        /// 这些筛选器仅为特定 Grain 类型执行，而非系统范围执行。
        /// 演示：
        /// - 使用自定义逻辑拦截方法
        /// - 选择性筛选（不拦截某些方法）
        /// - 访问实现方法的元数据
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GrainLevel_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(0);
            var result = await grain.One();
            Assert.Equal("intercepted one with no args", result);

            result = await grain.Echo("stao erom tae");
            Assert.Equal("eat more oats", result);// Grain 拦截器应收到实现方法的 MethodInfo，而不是接口方法的 MethodInfo。

            result = await grain.NotIntercepted();
            Assert.Equal("not intercepted", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
        }

        /// <summary>
        /// 验证 Grain 调用筛选器能够正确处理泛型 Grain 类型。
        /// 泛型 Grain 给基于反射的系统带来了特殊挑战，
        /// 因此此测试确保筛选器可以正确拦截泛型 Grain 方法的调用。
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
        /// 测试实现同一泛型接口的多个构造类型的 Grain 上的筛选器。
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
        /// 演示 Grain 调用筛选器中的异常处理。
        /// 筛选器可以捕获 Grain 方法抛出的异常，并执行以下操作：
        /// - 将异常转换为其他响应
        /// - 记录日志并重新抛出
        /// - 转换为特定领域的异常
        /// 此测试演示将异常转换为成功响应。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_ExceptionHandling_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 Grain 方法会抛出异常，但其中一个筛选器应处理该异常并将其转换
            // 为指定消息。
            var result = await grain.Throw();
            Assert.NotNull(result);
            Assert.Equal("EXCEPTION! Oi!", result);
        }

        /// <summary>
        /// 测试 Grain 调用筛选器能够抛出异常。
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
        /// 测试 Grain 调用筛选器为 <see cref="Orleans.IGrainCallContext.Result"/> 设置错误的结果类型时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_SetIncorrectResultType_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 此 Grain 方法会抛出异常，但其中一个筛选器应处理该异常并将其转换
            // 为指定消息。（译注：当前场景实际由筛选器设置错误类型的结果，并断言 InvalidCastException。）
            await Assert.ThrowsAsync<InvalidCastException>(() => grain.IncorrectResultType());
        }

        /// <summary>
        /// 测试 Grain 调用筛选器是否正确拦截对 Grain 扩展的调用。
        /// Grain 扩展是一种为现有 Grain 添加附加接口的方式。
        /// 此测试验证即使通过扩展接口而非主 Grain 接口调用方法，
        /// 筛选器也能正确工作。
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
        /// 验证 Grain 实现泛型接口时的方法解析正确性。
        /// 测试以下复杂继承场景：
        /// - Grain 实现多个泛型接口
        /// - 不同接口中的方法具有相同名称
        /// - 筛选器必须正确识别所调用的接口方法
        /// 这对于需要应用特定接口逻辑的筛选器至关重要。
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
        /// 测试 Grain 调用筛选器不调用 <see cref="IGrainCallContext.Invoke"/> 时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_SystemWideDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 调用筛选器未继续执行 Invoke 链，但错误状态应以
            // InvalidOperationException 的形式抛出，而不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
        }

        /// <summary>
        /// 测试 Grain 调用筛选器不调用 <see cref="IGrainCallContext.Invoke"/> 时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Incoming_GrainSpecificDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IGrainCallFilterTestGrain>(Random.Shared.Next());

            // 调用筛选器未继续执行 Invoke 链，但错误状态应以
            // InvalidOperationException 的形式抛出，而不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GrainSpecificCallFilterMarker());
        }

        /// <summary>
        /// 测试出站 Grain 调用筛选器不调用 <see cref="IGrainCallContext.Invoke"/> 时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task GrainCallFilter_Outgoing_SystemWideDoesNotCallContextInvoke_Test()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IMethodInterceptionGrain>(Random.Shared.Next());

            // 调用筛选器未继续执行 Invoke 链，但错误状态应以
            // InvalidOperationException 的形式抛出，而不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
        }

        /// <summary>
        /// 确保 Grain 调用筛选器以正确顺序在方法调用前后执行。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_Order_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 此 Grain 方法读取上下文并返回它。
            var context = await grain.GetRequestContext();
            Assert.NotNull(context);
            Assert.Equal("1234", context);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试入站调用筛选器能够重试对观察者的调用。
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
        /// 测试入站调用筛选器能够作用于使用 HashSet 的观察者。
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
        /// 测试 Grain 调用筛选器不调用 <see cref="IGrainCallContext.Invoke"/> 时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_SystemWideDoesNotCallContextInvoke_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 调用筛选器未继续执行 Invoke 链，但错误状态应以
            // InvalidOperationException 的形式抛出，而不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SystemWideCallFilterMarker());
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试 Grain 调用筛选器不调用 <see cref="IGrainCallContext.Invoke"/> 时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_GrainSpecificDoesNotCallContextInvoke_Test()
        {
            var observer = new GrainCallFilterTestGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IGrainCallFilterTestGrainObserver>(observer);

            // 调用筛选器未继续执行 Invoke 链，但错误状态应以
            // InvalidOperationException 的形式抛出，而不是 NullReferenceException。
            await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GrainSpecificCallFilterMarker());
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 仅测试 Grain 级别的筛选器。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_GrainLevel_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);
            var result = await grain.One();
            Assert.Equal("intercepted one with no args", result);

            result = await grain.Echo("stao erom tae");
            Assert.Equal("eat more oats", result);// Grain 拦截器应收到实现方法的 MethodInfo，而不是接口方法的 MethodInfo。

            result = await grain.NotIntercepted();
            Assert.Equal("not intercepted", result);

            result = await grain.SayHello();
            Assert.Equal("Hello", result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试泛型 Grain 上的筛选器。
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
        /// 测试实现同一泛型接口的多个构造类型的 Grain 上的筛选器。
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
        /// 测试 Grain 调用筛选器能够处理异常。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_ExceptionHandling_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);

            // 此 Grain 方法会抛出异常，但其中一个筛选器应处理该异常并将其转换
            // 为指定消息。
            var result = await grain.Throw();
            Assert.NotNull(result);
            Assert.Equal("EXCEPTION! Oi!", result);
            GC.KeepAlive(observer);
        }

        /// <summary>
        /// 测试 Grain 调用筛选器能够抛出异常。
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
        /// 测试 Grain 调用筛选器为 <see cref="Orleans.IGrainCallContext.Result"/> 设置错误的结果类型时，
        /// 调用方收到异常。
        /// </summary>
        [Fact]
        public async Task Observer_GrainCallFilter_Incoming_SetIncorrectResultType_Test()
        {
            var observer = new MethodInterceptionGrainObserver();
            var grain = this.fixture.GrainFactory.CreateObjectReference<IMethodInterceptionGrainObserver>(observer);

            // 此 Grain 方法会抛出异常，但其中一个筛选器应处理该异常并将其转换
            // 为指定消息。（译注：当前场景实际由筛选器设置错误类型的结果，并断言 InvalidCastException。）
            await Assert.ThrowsAsync<InvalidCastException>(() => grain.IncorrectResultType());
            GC.KeepAlive(observer);
        }
    }
}

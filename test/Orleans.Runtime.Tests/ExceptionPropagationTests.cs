using TestExtensions;

using UnitTests.GrainInterfaces;

using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 全面测试例外情形,在 Orleans分布式系统边界上传播。
    /// 
    /// Orleans 必须正确地向调用者宣传从 grains 返回的例外, 并保存:
    /// - 原始例外类型和讯息
    /// - 远程 grain (对调试分布式系统关键)的堆积痕迹
    /// - AggregateException 结构(未解包)
    /// - 任务取消语义
    /// 
    /// 这些测试还核查了序列/下层机能失效期间的例外传播情况,
    /// 可在多个点发生:
    /// - 客户客户 - > Grain (要求/答复)
    /// - Grain - > 客户端(回召)
    /// - Grain -- > Grain (交叉silo 调用)
    /// 
    /// 适当的例外处理对调试和维护分布式应用程序至关重要。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ExceptionPropagationTests : OrleansTestingBase, IClassFixture<ExceptionPropagationTests.Fixture>
    {
        private const int TestIterations = 3;
        private readonly ITestOutputHelper output;
        private readonly Fixture fixture;
        private readonly IMessageSerializationGrain exceptionGrain;
        private readonly MessageSerializationClientObject clientObject = new MessageSerializationClientObject();
        private readonly IMessageSerializationClientObject clientObjectRef;

        public ExceptionPropagationTests(ITestOutputHelper output, Fixture fixture)
        {
            this.output = output;
            this.fixture = fixture;

            var grainFactory = (IInternalGrainFactory)this.fixture.GrainFactory;
            this.exceptionGrain = grainFactory.GetGrain<IMessageSerializationGrain>(GetRandomGrainId());
            this.clientObjectRef = grainFactory.CreateObjectReference<IMessageSerializationClientObject>(this.clientObject);
        }

        public class Fixture : BaseTestClusterFixture
        {
        }

        /// <summary>
        /// 证明 grains 提出的不同例外类型正确无误
        /// 以原始类型和讯息完整地向呼叫客户传播
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionsPropagatedFromGrainToClient()
        {
            var grain = this.fixture.Client.GetGrain<IExceptionGrain>(0);

            var invalidOperationException = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ThrowsInvalidOperationException());
            Assert.Equal("Test exception", invalidOperationException.Message);

            var nullReferenceException = await Assert.ThrowsAsync<NullReferenceException>(() => grain.ThrowsNullReferenceException());
            Assert.Equal("null null null", nullReferenceException.Message);
        }

        /// <summary>
        /// 确保采用 grain 方法的例外情形的基本检验
        /// 与同一例外类型一样,被传播回至呼叫者。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task BasicExceptionPropagation()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => grain.ThrowsInvalidOperationException());

            output.WriteLine(exception.ToString());
            Assert.Equal("Test exception", exception.Message);
        }

        /// <summary>
        /// 关键测试,以核实在传播的例外情况中是否保存了远距离堆叠痕迹。
        /// 当调试分布式系统时, 关键是要查看例外的起源
        /// 偏僻的 grain不只是客户收到地址
        /// 使用. Wait () 来获取原始 AggregateException ,而不进行同步状态机器的修改 。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionContainsOriginalStackTrace()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());

            // 明确使用.wait () 而不是等待任务以避免对内特例作任何修改
            var originalException = await Task.Run(() =>
            {
                try
                {
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
                    grain.ThrowsInvalidOperationException().Wait();
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            var aggEx = Assert.IsType<AggregateException>(originalException);

            var exception = aggEx.InnerException!;
            output.WriteLine(exception.ToString());
            Assert.IsAssignableFrom<InvalidOperationException>(exception);
            Assert.Equal("Test exception", exception.Message);
            Assert.Contains("ThrowsInvalidOperationException", exception.StackTrace);
        }

        /// <summary>
        /// 证明即使使用async/wait(重新播种例外),
        /// 原始远程书架跟踪仍然保存在例外中。
        /// 这对于保持合成代码中的可调试性十分重要。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionContainsOriginalStackTraceWhenRethrowingLocally()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            try
            {
                // 使用该工具以迫迫重新丢回例外,并验证远程堆藏追踪仍然存在
                await grain.ThrowsInvalidOperationException();
                Assert.Fail("should have thrown");
            }
            catch (InvalidOperationException exception)
            {
                output.WriteLine(exception.ToString());
                Assert.IsAssignableFrom<InvalidOperationException>(exception);
                Assert.Equal("Test exception", exception.Message);
                Assert.Contains("ThrowsInvalidOperationException", exception.StackTrace);
            }
        }

        /// <summary>
        /// 确保: AggregateExceptions 被 grains 在传播期间不解包。
        /// 这保留了原有的例外结构,对处理错误可能很重要
        /// 逻辑预示了特定的例外等级。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionPropagationDoesNotUnwrapAggregateExceptions()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var exception = await Assert.ThrowsAsync<AggregateException>(
                () => grain.ThrowsAggregateExceptionWrappingInvalidOperationException());

            var nestedEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerException);
            Assert.Equal("Test exception", nestedEx.Message);
        }

        /// <summary>
        /// 嵌套 AggregateExceptions 在传播期间保持结构的测试。
        /// Orleans 保存原巢,不平整例外等级,以保持原巢
        /// 可能具有语义含义的词句在应用程序中。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionPropagationDoesNoFlattenAggregateExceptions()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var exception = await Assert.ThrowsAsync<AggregateException>(
                () => grain.ThrowsNestedAggregateExceptionsWrappingInvalidOperationException());

            var nestedAggEx = Assert.IsAssignableFrom<AggregateException>(exception.InnerException);
            var doubleNestedEx = Assert.IsAssignableFrom<InvalidOperationException>(nestedAggEx.InnerException);
            Assert.Equal("Test exception", doubleNestedEx.Message);
        }

        /// <summary>
        /// 证明取消任务作为 TaskCanceledException适当宣传。
        /// 这对于分布式系统中的合作取消模式十分重要。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task TaskCancelationPropagation()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => grain.Canceled());
        }

        /// <summary>
        /// 通过 grain- -grain 电话进行测试例外传播。
        /// 何时 grain 调来电话 grain B, 和 grain B会破例
        /// 例外情况应该通过 grain A向原调用者宣传。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task GrainForwardingExceptionPropagation()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var otherGrainId = GetRandomGrainId();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => grain.GrainCallToThrowsInvalidOperationException(otherGrainId));

            Assert.Equal("Test exception", exception.Message);
        }

        [Fact, TestCategory("BVT")]
        public async Task GrainForwardingExceptionPropagationDoesNotUnwrapAggregateExceptions()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var otherGrainId = GetRandomGrainId();
            var exception = await Assert.ThrowsAsync<AggregateException>(
                () => grain.GrainCallToThrowsAggregateExceptionWrappingInvalidOperationException(otherGrainId));

            var nestedEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerException);
            Assert.Equal("Test exception", nestedEx.Message);
        }

        /// <summary>
        /// 确保例外情形同步地采用 grain 方法(在等待任何等待之前)
        /// 而不是同步向打电话者投掷。
        /// 无论在何地出现例外,这都保持了一贯的同步行为。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SynchronousExceptionThrownShouldResultInFaultedTask()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());

            // 开始 grain 调用,但不要等待它或以试捕/抓获方式包扎,以确保它不会同步投
            var grainCallTask = grain.ThrowsSynchronousInvalidOperationException();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => grainCallTask);

            Assert.Equal("Test exception", exception.Message);

            var grainCallTask2 = grain.ThrowsSynchronousInvalidOperationException();
            var exception2 = await Assert.ThrowsAsync<InvalidOperationException>(() => grainCallTask2);
            Assert.Equal("Test exception", exception2.Message);
        }

        /// <summary>
        /// 测试当任务有多重例外被错责时,所有例外
        /// 传播在 AggregateException中,而不仅仅是第一个。
        /// 在执行第1378号议题之前,目前暂不讨论这个问题。
        /// </summary>
        [Fact(Skip = "Implementation of issue #1378 is still pending"), TestCategory("BVT")]
        public async Task ExceptionPropagationForwardsEntireAggregateException()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            var grainCall = grain.ThrowsMultipleExceptionsAggregatedInFaultedTask();

            var originalException = await Task.Run(() =>
            {
                try
                {
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
                    // 使用 Wait () 以便我们得到整个 AggregateException (“ 等待” 将只抓住第一个内部例外)
                    // 不使用 Assert.Throws 来避免从测试框架对 AggregateException 本身作任何篡改
                    grainCall.Wait();
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            var exception = Assert.IsType<AggregateException>(originalException);

            output.WriteLine(exception.ToString());

            // 确保任务中的所有例外都存在,而不仅仅是第一项例外。
            Assert.Equal(2, exception.InnerExceptions.Count);
            var firstEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[0]);
            Assert.Equal("Test exception 1", firstEx.Message);
            var secondEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[1]);
            Assert.Equal("Test exception 2", secondEx.Message);
        }

        [Fact, TestCategory("BVT")]
        public async Task SynchronousAggregateExceptionThrownShouldResultInFaultedTaskWithOriginalAggregateExceptionUnmodifiedAsInnerException()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());

            // 开始 grain 调用,但不要等待它或以试捕/抓获方式包扎,以确保它不会同步投
            var grainCallTask = grain.ThrowsSynchronousAggregateExceptionWithMultipleInnerExceptions();

            // 声称错误任务有内型例外 AggregateException应该是我们原来的例外
            var exception = await Assert.ThrowsAsync<AggregateException>(() => grainCallTask);

            Assert.StartsWith("Test AggregateException message", exception.Message);

            // 确保任务中的所有例外都存在,而不仅仅是第一项例外。
            Assert.Equal(2, exception.InnerExceptions.Count);
            var firstEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[0]);
            Assert.Equal("Test exception 1", firstEx.Message);
            var secondEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[1]);
            Assert.Equal("Test exception 2", secondEx.Message);
        }

        /// <summary>
        /// 当客户无法解密 grain的请求时,测试例外传播。
        /// 如果 grain 回调一个客户观察员,但
        /// 请求中包含客户端无法解密的类型 。
        /// 应检测到序列化故障,并将其传播回 grain。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsClient_Request_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUndeserializableToClient(this.clientObjectRef));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试客户无法对 grain的回复进行序列处理时,例外被迅速转发给原调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsClient_Response_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.GetUnserializableFromClient(this.clientObjectRef));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果一个 grain 无法解除客户回复的顺序,则例外将迅速传播到最初的调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsClient_Response_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.GetUndeserializableFromClient(this.clientObjectRef));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果 grain 无法对客户端的请求进行序列处理,则例外被迅速转发回原调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsClient_Request_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUnserializableToClient(this.clientObjectRef));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 解密失败时, grain-grain 调用时的测试传播例外。
        /// 这可能发生在交叉...silo 假设情况中 grains 类型版本不兼容
        /// 或当使用自定义序列器失败时,该例外情形应该回传
        /// 透过呼叫链到最初的来电者
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsGrain_Request_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUndeserializableToOtherSilo());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果 grain 无法将请求序列化为另一个 grain,则例外将被迅速传播回原调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsGrain_Request_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUnserializableToOtherSilo());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果 grain 不能对另一个 grain的答复进行序列处理, 例外将被迅速传播回原调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsGrain_Response_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.GetUnserializableFromOtherSilo());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果一个 grain 无法解除另一个 grain的答复的顺序,则将例外迅速传播回原呼叫者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_GrainCallsGrain_Response_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.GetUndeserializableFromOtherSilo());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试最常见的序列化故障假设情景:客户发送请求
        /// 无法使 grain 降序。这验证了 Orleans 是否正确检测
        /// 失败并发回适当的例外情形给客户而不是
        /// 沉默失败或绞死。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_ClientCallsGrain_Request_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUndeserializable(new UndeserializableType(32)));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试客户端无法将向 grain提出的请求序列化时,例外将被迅速传播回原调用者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_ClientCallsGrain_Request_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<NotSupportedException>(() => exceptionGrain.SendUnserializable(new UnserializableType()));
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试如果 grain 无法对客户端回复进行序列处理,则例外将迅速传播回原呼叫者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_ClientCallsGrain_Response_Serialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<Exception>(() => exceptionGrain.GetUnserializable());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        /// <summary>
        /// 测试客户端无法解除 grain回复的序列,例外将被迅速传播回原呼叫者。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Messaging"), TestCategory("Serialization")]
        public async Task ExceptionPropagation_ClientCallsGrain_Response_Deserialization_Failure()
        {
            for (var i = 0; i < TestIterations; i++)
            {
                var exception = await Assert.ThrowsAnyAsync<Exception>(() => exceptionGrain.GetUndeserializable());
                Assert.Contains(UndeserializableType.FailureMessage, exception.Message);
            }
        }

        private class MessageSerializationClientObject : IMessageSerializationClientObject
        {
            public Task SendUndeserializable(UndeserializableType input) => Task.FromResult(input);
            public Task SendUnserializable(UnserializableType input) => Task.FromResult(input);
            public Task<UnserializableType> GetUnserializable() => Task.FromResult(new UnserializableType());
            public Task<UndeserializableType> GetUndeserializable() => Task.FromResult(new UndeserializableType(35));
        }
    }
}

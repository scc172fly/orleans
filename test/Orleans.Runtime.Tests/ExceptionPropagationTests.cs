using TestExtensions;

using UnitTests.GrainInterfaces;

using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 对跨越 Orleans 分布式系统边界的异常传播进行全面测试。
    /// 
    /// Orleans 必须正确将异常从 Grain 传播回调用方，并保留以下内容：
    /// - 原始异常类型和消息
    /// - 来自远程 Grain 的堆栈跟踪（对调试分布式系统至关重要）
    /// - AggregateException 的结构（不解包）
    /// - 任务取消语义
    /// 
    /// 这些测试还验证序列化或反序列化失败期间的异常传播，
    /// 此类失败可能出现在多个环节：
    /// - 客户端 -> Grain（请求/响应）
    /// - Grain -> 客户端（回调）
    /// - Grain -> Grain（跨 Silo 调用）
    /// 
    /// 正确处理异常对于调试和维护分布式应用至关重要。
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
        /// 验证 Grain 抛出的不同类型异常能否正确
        /// 传播至调用客户端，并完整保留其原始类型和消息。
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
        /// 基础测试，确保 Grain 方法抛出的异常
        /// 以相同异常类型传播回调用方。
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
        /// 关键测试，验证传播的异常中保留了远程堆栈跟踪。
        /// 调试分布式系统时，必须能够看到异常在远程 Grain 中的发生位置，
        /// 而不仅仅是客户端接收到异常的位置。
        /// 使用 .Wait() 获取原始 AggregateException，不经过异步状态机的修改。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionContainsOriginalStackTrace()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());

            // 显式使用 .Wait() 而不是 await 任务，避免对内部异常进行任何修改
            var originalException = await Task.Run(() =>
            {
                try
                {
#pragma warning disable xUnit1031 // 不要在测试方法中使用阻塞式任务操作
                    grain.ThrowsInvalidOperationException().Wait();
#pragma warning restore xUnit1031 // 不要在测试方法中使用阻塞式任务操作
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
        /// 验证即使使用 async/await（会重新抛出异常），
        /// 异常中仍保留原始远程堆栈跟踪。
        /// 这对于保持异步代码的可调试性很重要。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task ExceptionContainsOriginalStackTraceWhenRethrowingLocally()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            try
            {
                // 使用 await 强制重新抛出异常，并验证远程堆栈跟踪仍然存在
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
        /// 确保 Grain 抛出的 AggregateException 在传播时不被解包。
        /// 这保留了原始异常结构，对于依赖特定异常层级结构的
        /// 错误处理逻辑可能很重要。
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
        /// 测试嵌套的 AggregateException 在传播过程中保持原有结构。
        /// Orleans 不会将异常层级扁平化，而是保留原始嵌套结构，
        /// 该结构在应用中可能具有特定语义。
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
        /// 验证任务取消是否以 TaskCanceledException 正确传播。
        /// 这对于分布式系统中的协作式取消模式很重要。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task TaskCancelationPropagation()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => grain.Canceled());
        }

        /// <summary>
        /// 测试 Grain 间调用的异常传播。
        /// 当 Grain A 调用 Grain B，而 Grain B 抛出异常时，
        /// 该异常应经过 Grain A 传播回原始调用方。
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
        /// 确保 Grain 方法中同步抛出的异常（任何 await 之前）
        /// 仍然会产生处于故障状态的 Task，而不是同步向调用方抛出。
        /// 这样无论异常出现在哪里，都能保持一致的异步行为。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task SynchronousExceptionThrownShouldResultInFaultedTask()
        {
            IExceptionGrain grain = this.fixture.GrainFactory.GetGrain<IExceptionGrain>(GetRandomGrainId());

            // 发起 Grain 调用，但既不 await，也不用 try/catch 包裹，以确保调用不会同步抛出异常
            var grainCallTask = grain.ThrowsSynchronousInvalidOperationException();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => grainCallTask);

            Assert.Equal("Test exception", exception.Message);

            var grainCallTask2 = grain.ThrowsSynchronousInvalidOperationException();
            var exception2 = await Assert.ThrowsAsync<InvalidOperationException>(() => grainCallTask2);
            Assert.Equal("Test exception", exception2.Message);
        }

        /// <summary>
        /// 测试 Task 因多个异常进入故障状态时，所有异常
        /// 均在 AggregateException 中传播，而不只是第一个异常。
        /// 目前跳过此测试，等待 issue #1378 的实现。
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
#pragma warning disable xUnit1031 // 不要在测试方法中使用阻塞式任务操作
                    // 使用 Wait() 以获取整个 AggregateException（'await' 仅会捕获第一个内部异常）
                    // 不要使用 Assert.Throws，避免测试框架对 AggregateException 本身进行任何修改
                    grainCall.Wait();
#pragma warning restore xUnit1031 // 不要在测试方法中使用阻塞式任务操作
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            var exception = Assert.IsType<AggregateException>(originalException);

            output.WriteLine(exception.ToString());

            // 确保任务中的所有异常都存在，而不只是第一个异常。
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

            // 发起 Grain 调用，但既不 await，也不用 try/catch 包裹，以确保调用不会同步抛出异常
            var grainCallTask = grain.ThrowsSynchronousAggregateExceptionWithMultipleInnerExceptions();

            // 断言处于故障状态的任务包含 AggregateException 类型的内部异常，该异常应为我们的原始异常
            var exception = await Assert.ThrowsAsync<AggregateException>(() => grainCallTask);

            Assert.StartsWith("Test AggregateException message", exception.Message);

            // 确保任务中的所有异常都存在，而不只是第一个异常。
            Assert.Equal(2, exception.InnerExceptions.Count);
            var firstEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[0]);
            Assert.Equal("Test exception 1", firstEx.Message);
            var secondEx = Assert.IsAssignableFrom<InvalidOperationException>(exception.InnerExceptions[1]);
            Assert.Equal("Test exception 2", secondEx.Message);
        }

        /// <summary>
        /// 测试客户端无法反序列化来自 Grain 的请求时的异常传播。
        /// 当 Grain 回调客户端观察者，而请求中
        /// 包含客户端无法反序列化的类型时，会发生这种情况。
        /// 应检测到序列化失败，并将其传播回 Grain。
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
        /// 测试客户端无法序列化发送给 Grain 的响应时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 无法反序列化来自客户端的响应时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 无法序列化发送给客户端的请求时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 间调用反序列化失败时的异常传播。
        /// 当跨 Silo 场景中的 Grain 使用不兼容的类型版本，
        /// 或者自定义序列化器发生故障时，可能出现这种情况。异常应沿调用链
        /// 传播回原始调用方。
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
        /// 测试 Grain 无法序列化发送给另一个 Grain 的请求时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 无法序列化发送给另一个 Grain 的响应时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 无法反序列化另一个 Grain 的响应时，是否及时将异常传播回原始调用方。
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
        /// 测试最常见的序列化失败场景：客户端发送
        /// Grain 无法反序列化的请求。这验证 Orleans 能否正确检测
        /// 该失败并向客户端返回适当的异常，而不是
        /// 静默失败或挂起。
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
        /// 测试客户端无法序列化发送给 Grain 的请求时，是否及时将异常传播回原始调用方。
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
        /// 测试 Grain 无法序列化发送给客户端的响应时，是否及时将异常传播回原始调用方。
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
        /// 测试客户端无法反序列化来自 Grain 的响应时，是否及时将异常传播回原始调用方。
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

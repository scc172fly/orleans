using System.Globalization;
using Orleans.Internal;
using Orleans.Runtime;
using Xunit;

// ReSharper disable ConvertToLambdaExpression

namespace NonSilo.Tests
{
    /// <summary>
    /// 用于 AsyncExecutorWithRetries 通用类的测试,该类提供有再试逻辑的抗耐力非同步执行。
    /// 这种类测试 Orleans内部再试验机制,用于处理非同步操作中的瞬态故障。
    /// AsyncExecutorWithRetries 用于整个 Orleans 可靠地执行可能暂时失效的行动。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class Async_AsyncExecutorWithRetriesTests
    {
        private readonly ITestOutputHelper output;

        public Async_AsyncExecutorWithRetriesTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task MissingAction_IsRejectedWithoutRetrying()
        {
            var filterCalls = 0;
            var backoff = new CountingBackoff();
            bool Retry(Exception exception, int attempt)
            {
                filterCalls++;
                return true;
            }

            var error = await Record.ExceptionAsync(() => AsyncExecutorWithRetries.ExecuteWithRetries(
                (Func<int, Task>)null!, 3, Retry, Timeout.InfiniteTimeSpan, backoff));

            Assert.Equal(0, filterCalls);
            Assert.Equal(0, backoff.Attempts);
            var exception = Assert.IsType<ArgumentNullException>(error);
            Assert.Equal("action", exception.ParamName);
        }

        [Theory, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task MissingFunction_IsRejectedWithoutRetrying_PreservesCancellationPriority(
            bool withSuccessFilter, bool canceled)
        {
            var filterCalls = 0;
            var backoff = new CountingBackoff();
            var cancellationToken = canceled ? new CancellationToken(true) : TestContext.Current.CancellationToken;
            bool Retry(Exception exception, int attempt)
            {
                filterCalls++;
                return true;
            }

            var error = await Record.ExceptionAsync(() => withSuccessFilter
                ? AsyncExecutorWithRetries.ExecuteWithRetries<int>(
                    null!, 0, 3, null, Retry, Timeout.InfiniteTimeSpan, backoff, backoff, cancellationToken)
                : AsyncExecutorWithRetries.ExecuteWithRetries<int>(
                    null!, 3, Retry, Timeout.InfiniteTimeSpan, backoff, cancellationToken));

            Assert.Equal(0, filterCalls);
            Assert.Equal(0, backoff.Attempts);
            if (canceled)
            {
                var exception = Assert.IsType<OperationCanceledException>(error);
                Assert.Equal(cancellationToken, exception.CancellationToken);
            }
            else
            {
                var exception = Assert.IsType<ArgumentNullException>(error);
                Assert.Equal("function", exception.ParamName);
            }
        }

        private sealed class CountingBackoff : IBackoffProvider
        {
            public int Attempts { get; private set; }

            public TimeSpan Next(int attempt)
            {
                Attempts++;
                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// 测试 AsyncExecutorWithRetries 重写一个失败函数,直到它成功为止。 
        /// 并证实当重试超过极限时失败。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_1()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            int counter = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                // ReSharper disable AccessToModifiedClosure
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                counter++;
                if (counter == 5)
                    return Task.FromResult(28);
                else
                    throw new ArgumentException("Wrong arg!");
                // ReSharper restore AccessToModifiedClosure
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                return true;
            });

            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc, 10, 10, null, errorFilter, cancellationToken: cancellationToken);
            int value = await promise;
            this.output.WriteLine("Value is {0}.", value);
            counter = 0;
            try
            {
                promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                    myFunc, 3, 3, null, errorFilter, cancellationToken: cancellationToken);
                value = await promise;
                this.output.WriteLine("Value is {0}.", value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return;
            }
            Assert.Fail("Should have thrown");
        }

        /// <summary>
        /// 测试成功过滤功能,在满足成功条件之前,再试继续。
        /// 证明成功过滤器返回时执行停止。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_2()
        {
            int counter = 0;
            const int countLimit = 5;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                // ReSharper disable AccessToModifiedClosure
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                return Task.FromResult(++counter);
                // ReSharper restore AccessToModifiedClosure
            });
            Func<int, int, bool> successFilter = ((int count, int i) => count != countLimit);

            int maxRetries = 10;
            int expectedRetries = countLimit;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc,
                maxRetries,
                maxRetries,
                successFilter,
                null,
                Timeout.InfiniteTimeSpan,
                cancellationToken: TestContext.Current.CancellationToken);
            int value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, expectedRetries);
            Assert.Equal(expectedRetries, value); // “返回值”
            Assert.Equal(counter, value); // "Counter == 回报价值"

            counter = 0;
            maxRetries = 3;
            expectedRetries = maxRetries;
            promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc,
                maxRetries,
                maxRetries,
                successFilter,
                null,
                cancellationToken: TestContext.Current.CancellationToken);
            value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, expectedRetries);
            Assert.Equal(expectedRetries, value); // “返回值”
            Assert.Equal(counter, value); // "Counter == 回报价值"
        }

        /// <summary>
        /// 测试成功执行,无任何错误,核查是否未引用错误过滤器
        /// 当函数在第一次尝试中成功时。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_4()
        {
            int counter = 0;
            int lastIteration = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                lastIteration = funcCounter;
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running for {0} time.", counter);
                return Task.FromResult(++counter);
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                Assert.Equal(lastIteration, i);
                Assert.Fail("Should not be called");
                return true;
            });

            int maxRetries = 5;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc,
                maxRetries,
                errorFilter,
                default,
                new FixedBackoff(TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken);

            int value = await promise;
            this.output.WriteLine("Value={0} Counter={1} ExpectedRetries={2}", value, counter, 0);
            Assert.Equal(counter, value);
            Assert.Equal(1, counter);
        }

        /// <summary>
        /// 当返回真实( retrolly)、 虚假( stop) 或丢弃例外时, 测试错误过滤行为 。
        /// 证明执行器尊重错误过滤器关于是否继续重试的决定。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Async_AsyncExecutorWithRetriesTest_5()
        {
            int counter = 0;
            int lastIteration = 0;
            Func<int, Task<int>> myFunc = ((int funcCounter) =>
            {
                lastIteration = funcCounter;
                Assert.Equal(counter, funcCounter);
                this.output.WriteLine("Running FUNC for {0} time.", counter);
                ++counter;
                throw new ArgumentException(counter.ToString(CultureInfo.InvariantCulture));
            });
            Func<Exception, int, bool> errorFilter = ((Exception exc, int i) =>
            {
                this.output.WriteLine("Running ERROR FILTER for {0} time.", i);
                Assert.Equal(lastIteration, i);
                if (i == 0 || i == 1)
                    return true;
                else if (i == 2)
                    throw exc;
                else
                    return false;
            });

            int maxRetries = 5;
            Task<int> promise = AsyncExecutorWithRetries.ExecuteWithRetries(
                myFunc,
                maxRetries,
                errorFilter,
                default,
                new FixedBackoff(TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken);
            try
            {
                int value = await promise;
                Assert.Fail("Should have thrown");
            }
            catch (OperationCanceledException) when (TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                Exception baseExc = exc.GetBaseException();
                Assert.Equal(typeof(ArgumentException), baseExc.GetType());
                this.output.WriteLine("baseExc.GetType()={0} Counter={1}", baseExc.GetType(), counter);
                Assert.Equal(3, counter); // "Counter == 回报价值"
            }
        }
    }
}

// ReSharper restore ConvertToLambdaExpression

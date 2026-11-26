using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 执行观察员模式。
    /// 观察员使 grains 能够向客户或其他 grains发送通知
    /// 后回调界面。 这是用于按键的 Orleans机制
    /// 允许 grains 将国家变化通知有关各方
    /// 观察员被指为防止
    /// 内存泄漏,支持自动清理。
    /// </summary>
    [TestArea("Runtime")]
    public class ObserverTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly TimeSpan timeout = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10);
        private int callbackCounter;
        private readonly bool[] callbacksReceived = new bool[2];

        // 我们保留观察员对象作为实例变量,以防止它们
        // 垃圾过早收集(运行时将它们作为薄弱的参考文献储存)。
        private SimpleGrainObserver? observer1;
        private SimpleGrainObserver? observer2;

        public ObserverTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        private void TestInitialize()
        {
            callbackCounter = 0;
            callbacksReceived[0] = false;
            callbacksReceived[1] = false;

            observer1 = null;
            observer2 = null;
        }

        private ISimpleObserverableGrain GetGrain()
        {
            return this.GrainFactory.GetGrain<ISimpleObserverableGrain>(GetRandomGrainId());
        }

        /// <summary>
        /// 测试观察员的基本通知功能。
        /// 证明a grain 可就国家变化向已登记观察员发出通知,
        /// 多次通知的递送正确无误,而且
        /// 正确维护了对观察员回调的背景。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public async Task ObserverTest_SimpleNotification()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_SimpleNotification_Callback, result, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(this.observer1);
            await grain.Subscribe(reference);
            await grain.SetA(3);
            await grain.SetB(2);

            Assert.True(await result.WaitForFinished(timeout));

            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
        }

        /// <summary>
        /// 使用产生的工厂方法测试观察员通知。
        /// 与 SimpleNotification 测试类似,但使用产生的工厂型态
        /// 建立观察员参考,核查两种方法都同样有效。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public async Task ObserverTest_SimpleNotification_GeneratedFactory()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_SimpleNotification_Callback, result, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            await grain.Subscribe(reference);
            await grain.SetA(3);
            await grain.SetB(2);

            Assert.True(await result.WaitForFinished(timeout));

            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
        }

        private void ObserverTest_SimpleNotification_Callback(int a, int b, AsyncResultHandle result)
        {
            callbackCounter++;
            this.Logger.LogInformation("Invoking ObserverTest_SimpleNotification_Callback for {CallbackCounter} time with a = {A} and b = {B}", this.callbackCounter, a, b);

            if (a == 3 && b == 0)
                callbacksReceived[0] = true;
            else if (a == 3 && b == 2)
                callbacksReceived[1] = true;
            else
                throw new ArgumentOutOfRangeException("Unexpected callback with values: a=" + a + ",b=" + b);

            if (callbackCounter == 1)
            {
                // 允许按任何顺序进行回调
                Assert.True(callbacksReceived[0] || callbacksReceived[1]);
            }
            else if (callbackCounter == 2)
            {
                Assert.True(callbacksReceived[0] && callbacksReceived[1]);
                result.Done = true;
            }
            else
            {
                Assert.True(false);
            }
        }

        /// <summary>
        /// 防止两次签署同一观察员的参考书的测试。
        /// 证明: Orleans 检测和拒绝重复订阅
        /// 防止重复通知,并保持订阅的完整性。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT")]
        public async Task ObserverTest_DoubleSubscriptionSameReference()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_DoubleSubscriptionSameReference_Callback, result, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            await grain.Subscribe(reference);
            await grain.SetA(1); // 使用 grain
            try
            {
                await grain.Subscribe(reference);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception exc)
            {
                Exception baseException = exc.GetBaseException();
                this.Logger.LogInformation(baseException, "Received exception");
                Assert.IsAssignableFrom<OrleansException>(baseException);
                if (!baseException.Message.StartsWith("Cannot subscribe already subscribed observer", StringComparison.Ordinal))
                {
                    Assert.Fail("Unexpected exception message: " + baseException);
                }
            }

            await grain.SetA(2); // 使用 grain

            Assert.False(await result.WaitForFinished(timeout), $"Should timeout waiting {timeout} for SetA(2)");

            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
        }

        private void ObserverTest_DoubleSubscriptionSameReference_Callback(int a, int b, AsyncResultHandle result)
        {
            callbackCounter++;
            this.Logger.LogInformation("Invoking ObserverTest_DoubleSubscriptionSameReference_Callback for {CallbackCounter} time with a={A} and b={B}", this.callbackCounter, a, b);
            Assert.True(callbackCounter <= 2, "Callback has been called more times than was expected " + callbackCounter);
            if (callbackCounter == 2)
            {
                result.Continue = true;
            }
        }

        /// <summary>
        /// 测试观察员的订阅/不订阅生命周期。
        /// 证明观察员在签署后收到通知,
        /// 取消订阅后停止接收,
        /// 操作中适当清理订阅。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT")]
        public async Task ObserverTest_SubscribeUnsubscribe()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_SubscribeUnsubscribe_Callback, result, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            await grain.Subscribe(reference);
            await grain.SetA(5);
            Assert.True(await result.WaitForContinue(timeout), $"Should not timeout waiting {timeout} for SetA");

            await grain.Unsubscribe(reference);
            await grain.SetB(3);

            Assert.False(await result.WaitForFinished(timeout), $"Should timeout waiting {timeout} for SetB");

            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
        }

        private void ObserverTest_SubscribeUnsubscribe_Callback(int a, int b, AsyncResultHandle result)
        {
            callbackCounter++;
            this.Logger.LogInformation("Invoking ObserverTest_SubscribeUnsubscribe_Callback for {CallbackCounter} time with a = {A} and b = {B}", this.callbackCounter, a, b);
            Assert.True(callbackCounter < 2, "Callback has been called more times than was expected.");

            Assert.Equal(5, a);
            Assert.Equal(0, b);

            result.Continue = true;
        }


        /// <summary>
        /// 测试取消了订阅观察员的签名,但从未被签署过。
        /// 证明试图取消不订阅的
        /// 处理优雅,不会造成系统错误。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public async Task ObserverTest_Unsubscribe()
        {
            TestInitialize();
            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(null, null, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            try
            {
                await grain.Unsubscribe(reference);

                this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception exc)
            {
                Exception baseException = exc.GetBaseException();
                if (!(baseException is OrleansException))
                    Assert.True(false);
            }
        }

        /// <summary>
        /// 测试多个不同的观察员是否同时加入同一 grain。
        /// 证明 grain 能够维持多个观察员订阅
        /// 并正确通知所有已登记观察员国家变更。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public async Task ObserverTest_DoubleSubscriptionDifferentReferences()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_DoubleSubscriptionDifferentReferences_Callback, result, this.Logger);
            ISimpleGrainObserver reference1 = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            this.observer2 = new SimpleGrainObserver(this.ObserverTest_DoubleSubscriptionDifferentReferences_Callback, result, this.Logger);
            ISimpleGrainObserver reference2 = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer2);
            await grain.Subscribe(reference1);
            await grain.Subscribe(reference2);
            grain.SetA(6).Ignore();

            Assert.True(await result.WaitForFinished(timeout), $"Should not timeout waiting {timeout} for SetA");

            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference1);
            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference2);
        }

        private void ObserverTest_DoubleSubscriptionDifferentReferences_Callback(int a, int b, AsyncResultHandle result)
        {
            callbackCounter++;
            this.Logger.LogInformation("Invoking ObserverTest_DoubleSubscriptionDifferentReferences_Callback for {CallbackCounter} time with a = {A} and b = {B}", this.callbackCounter, a, b);
            Assert.True(callbackCounter < 3, "Callback has been called more times than was expected.");

            Assert.Equal(6, a);
            Assert.Equal(0, b);

            if (callbackCounter == 2)
                result.Done = true;
        }

        /// <summary>
        /// 测试删除观察员引用时停止通知。
        /// 证明,在使用
        /// DeleteObjectReference, 和 grain 无法再发送通知
        /// 展示了自动清理行为
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT")]
        public async Task ObserverTest_DeleteObject()
        {
            TestInitialize();
            var result = new AsyncResultHandle();

            ISimpleObserverableGrain grain = GetGrain();
            this.observer1 = new SimpleGrainObserver(this.ObserverTest_DeleteObject_Callback, result, this.Logger);
            ISimpleGrainObserver reference = this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(observer1);
            await grain.Subscribe(reference);
            await grain.SetA(5);
            Assert.True(await result.WaitForContinue(timeout), $"Should not timeout waiting {timeout} for SetA");
            this.GrainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
            await grain.SetB(3);

            Assert.False(await result.WaitForFinished(timeout), $"Should timeout waiting {timeout} for SetB");
        }

        private void ObserverTest_DeleteObject_Callback(int a, int b, AsyncResultHandle result)
        {
            callbackCounter++;
            this.Logger.LogInformation("Invoking ObserverTest_DeleteObject_Callback for {CallbackCounter} time with a = {A} and b = {B}", this.callbackCounter, a, b);
            Assert.True(callbackCounter < 2, "Callback has been called more times than was expected.");

            Assert.Equal(5, a);
            Assert.Equal(0, b);

            result.Continue = true;
        }

        /// <summary>
        /// 证明只有 grain 参考文献可以作为观察员使用。
        /// 试图订阅正正对象的测试(不是通过
        /// CreateObjectReference提出适当的例外,强制执行
        /// 要求观察员必须作为 grain 参考。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public async Task ObserverTest_SubscriberMustBeGrainReference()
        {
            TestInitialize();
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                var result = new AsyncResultHandle();

                ISimpleObserverableGrain grain = this.GetGrain();
                this.observer1 = new SimpleGrainObserver(this.ObserverTest_SimpleNotification_Callback, result, this.Logger);
                ISimpleGrainObserver reference = this.observer1;
                // 应该是: this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(obj);
                await grain.Subscribe(reference);
                // 未达到
            });
        }

        /// <summary>
        /// 测试 CreateObjectReference 是否正确验证其参数。
        /// 证明试图从无效类型创建对象引用
        /// (类似 Grain 类或现有的 grain 引用) 投出合适的
        /// 例外,确保观察员模式的类型安全。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public void ObserverTest_CreateObjectReference_ThrowsForInvalidArgumentTypes()
        {
            TestInitialize();

            // 试图创建 Grain 类的对象引用。
            Assert.Throws<ArgumentException>(() => this.Client.CreateObjectReference<ISimpleGrainObserver>(new DummyObserverGrain()));

            // 试图创建引用现有 GrainReference的对象。
            var observer = new DummyObserver();
            var reference = this.Client.CreateObjectReference<ISimpleGrainObserver>(observer);
            Assert.Throws<ArgumentException>(() => this.Client.CreateObjectReference<ISimpleGrainObserver>(reference));
        }

        public class DummyObserverGrain : Grain, ISimpleGrainObserver
        {
            public void StateChanged(int a, int b) { }
        }

        public class DummyObserver : ISimpleGrainObserver
        {
            public void StateChanged(int a, int b) { }
        }

        internal class SimpleGrainObserver : ISimpleGrainObserver
        {
            private readonly Action<int, int, AsyncResultHandle>? action;
            private readonly AsyncResultHandle? result;

            private readonly ILogger logger;

            public SimpleGrainObserver(Action<int, int, AsyncResultHandle>? action, AsyncResultHandle? result, ILogger logger)
            {
                this.action = action;
                this.result = result;
                this.logger = logger;
            }

            public void StateChanged(int a, int b)
            {
                this.logger.LogDebug("SimpleGrainObserver.StateChanged a={A} b={B}", a, b);
                action?.Invoke(a, b, result!);
            }
        }
    }
}

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
    /// Orleans 观察者模式实现的测试。
    /// 观察者使 Grain 能通过回调接口向客户端或其他 Grain
    /// 发送通知。这是 Orleans 基于推送的通信机制，
    /// 使 Grain 能向相关方通知状态变化或事件，
    /// 无需轮询。观察者以弱引用方式保留，以防止
    /// 内存泄漏并支持自动清理。
    /// </summary>
    [TestArea("Runtime")]
    public class ObserverTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly TimeSpan timeout = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10);
        private int callbackCounter;
        private readonly bool[] callbacksReceived = new bool[2];

        // 将观察者对象保留为实例变量，以防止它们被过早
        // 垃圾回收（运行时以弱引用存储它们）。
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
        /// 测试基本观察者通知功能。
        /// 验证 Grain 能向已注册观察者通知状态变化，
        /// 多条通知均正确投递，并且观察者的
        /// 回调上下文得到正确保留。
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
        /// 测试使用生成的工厂方法进行观察者通知。
        /// 类似 SimpleNotification 测试，但使用生成的工厂模式
        /// 创建观察者引用，验证两种方式的行为完全一致。
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
                // 允许回调按任意顺序发生
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
        /// 测试是否禁止对同一观察者引用重复订阅两次。
        /// 验证 Orleans 检测并拒绝重复订阅，
        /// 防止重复通知并保持订阅完整性。
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
            await grain.SetA(1); // 使用 Grain
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

            await grain.SetA(2); // 使用 Grain

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
        /// 测试观察者订阅和取消订阅的生命周期。
        /// 验证观察者订阅后收到通知，
        /// 取消订阅后停止收到通知，且取消订阅操作
        /// 能正确清理订阅。
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
        /// 测试取消订阅从未订阅过的观察者。
        /// 验证尝试取消不存在的订阅时，
        /// 能够妥善处理，而不会导致系统错误。
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
        /// 测试多个不同观察者订阅同一 Grain。
        /// 验证 Grain 可以维护多个观察者订阅，
        /// 并正确通知所有已注册观察者状态变化。
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
        /// 测试删除观察者引用是否停止通知。
        /// 验证使用 DeleteObjectReference 删除观察者引用后，
        /// Grain 无法再向该观察者发送通知，
        /// 展示自动清理行为。
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
        /// 验证只有 Grain 引用可以用作观察者。
        /// 测试尝试订阅普通对象（而非通过
        /// CreateObjectReference 创建的对象）时会引发适当的异常，
        /// 从而强制要求观察者必须为 Grain 引用。
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
                // 应为：this.GrainFactory.CreateObjectReference<ISimpleGrainObserver>(obj);
                await grain.Subscribe(reference);
                // 不会执行到此处
            });
        }

        /// <summary>
        /// 测试 CreateObjectReference 能否正确验证其参数。
        /// 验证尝试从无效类型创建对象引用时，
        /// （例如 Grain 类或现有 Grain 引用）会引发适当的
        /// 异常，从而确保观察者模式中的类型安全。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT")]
        public void ObserverTest_CreateObjectReference_ThrowsForInvalidArgumentTypes()
        {
            TestInitialize();

            // 尝试创建指向 Grain 类的对象引用。
            Assert.Throws<ArgumentException>(() => this.Client.CreateObjectReference<ISimpleGrainObserver>(new DummyObserverGrain()));

            // 尝试创建指向现有 GrainReference 的对象引用。
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

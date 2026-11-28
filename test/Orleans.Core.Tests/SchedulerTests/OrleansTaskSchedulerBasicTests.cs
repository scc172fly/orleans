using Microsoft.Extensions.Logging;
using Orleans.Runtime.Scheduler;
using UnitTests.TesterInternal;
using Xunit;
using Orleans.TestingHost.Utils;

// ReSharper disable ConvertToConstant.Local

namespace UnitTests.SchedulerTests
{
    /// <summary>
    /// IGrainContext 的测试实现，用于对 Orleans 任务调度器进行单元测试，无需完整的 Grain 激活实例。
    /// </summary>
    internal class UnitTestSchedulingContext : IGrainContext, IDisposable
    {
        public static UnitTestSchedulingContext Create(ILoggerFactory loggerFactory)
        {
            var result = new UnitTestSchedulingContext();
            result.WorkItemGroup = SchedulingHelper.CreateWorkItemGroupForTesting(result, loggerFactory, out result._activationServices);
            return result;
        }

        private UnitTestSchedulingContext() { }

        private IServiceProvider _activationServices = null!;

        public WorkItemGroup WorkItemGroup { get; private set; } = null!;

        public GrainReference GrainReference => throw new NotImplementedException();

        public GrainId GrainId => throw new NotImplementedException();

        public IAddressable GrainInstance => throw new NotImplementedException();

        public ActivationId ActivationId => throw new NotImplementedException();

        public GrainAddress Address => throw new NotImplementedException();

        public IServiceProvider ActivationServices => _activationServices;

        public IDictionary<object, object> Items => throw new NotImplementedException();

        public IGrainLifecycle ObservableLifecycle => throw new NotImplementedException();

        public IWorkItemScheduler Scheduler => WorkItemGroup;

        public bool IsExemptFromCollection => throw new NotImplementedException();

        public PlacementStrategy PlacementStrategy => throw new NotImplementedException();

        object IGrainContext.GrainInstance => throw new NotImplementedException();

        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken) { }
        public Task Deactivated => Task.CompletedTask;
        public void Dispose()
        {
            (Scheduler as IDisposable)?.Dispose();
            (_activationServices as IDisposable)?.Dispose();
        }
        public object GetComponent(Type componentType) => throw new NotImplementedException();
        public object GetTarget() => throw new NotImplementedException();
        public void ReceiveMessage(object message) => throw new NotImplementedException();

        public void SetComponent<TComponent>(TComponent? value) where TComponent : class => throw new NotImplementedException();

        bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);
        void IGrainContext.Rehydrate(IRehydrationContext context) => throw new NotImplementedException();
        void IGrainContext.Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    /// <summary>
    /// Orleans 任务调度器的基础测试，该调度器是负责调度 Grain 工作项的核心组件。
    /// 调度器在高效利用线程池资源的同时，确保 Grain 的单线程执行语义。
    /// 这些测试无需完整的 Orleans 运行时即可验证基本调度操作。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Scheduler")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class OrleansTaskSchedulerBasicTests : IDisposable
    {
        private static readonly object Lockable = new();
        private readonly ITestOutputHelper _output;
        private readonly UnitTestSchedulingContext _rootContext;
        private readonly ILoggerFactory _loggerFactory;
        public OrleansTaskSchedulerBasicTests(ITestOutputHelper output)
        {
            _output = output;
            SynchronizationContext.SetSynchronizationContext(null);
            _loggerFactory = InitSchedulerLogging();
            _rootContext = UnitTestSchedulingContext.Create(_loggerFactory);
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }

        /// <summary>
        /// 测试能否在 Orleans 激活实例任务调度器上调度和执行任务。
        /// 验证 Grain 单线程执行上下文中的基本任务执行流程。
        /// </summary>
        [Fact, TestCategory("AsynchronyPrimitives")]
        public async Task Async_Task_Start_ActivationTaskScheduler()
        {
            var expected = 2;
            var done = false;
            var t = new Task<int>(() => { done = true; return expected; });
            _rootContext.Scheduler.QueueTask(t);

            var received = await t;
            Assert.True(t.IsCompleted, "Task should have completed");
            Assert.False(t.IsFaulted, "Task should not thrown exception: " + t.Exception);
            Assert.True(done, "Task should be done");
            Assert.Equal(expected, received);
        }

        [Theory]
        [InlineData("task", false)]
        [InlineData("task", true)]
        [InlineData("task-result", false)]
        [InlineData("task-result", true)]
        [InlineData("value-task", false)]
        [InlineData("value-task", true)]
        [InlineData("action", false)]
        public async Task ClosureWorkItem_PropagatesDelegateFailureThroughCompletionTask(string kind, bool delayedFailure)
        {
            var expected = new InvalidOperationException("Work-item delegate failed.");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WorkItemBase workItem;
            Task completion;
            switch (kind)
            {
                case "task":
                    var asyncItem = new AsyncClosureWorkItem(
                        () => delayedFailure ? FailAsync() : throw expected, _rootContext);
                    workItem = asyncItem;
                    completion = asyncItem.Task;
                    break;
                case "task-result":
                    var resultItem = new AsyncClosureWorkItem<int>(
                        () => delayedFailure ? FailAsync() : throw expected, _rootContext);
                    workItem = resultItem;
                    completion = resultItem.Task;
                    break;
                case "value-task":
                    var valueTaskItem = new StatefulAsyncClosureWorkItem<Exception>(
                        failure => delayedFailure ? new ValueTask(FailAsync()) : throw failure,
                        expected, _rootContext);
                    workItem = valueTaskItem;
                    completion = valueTaskItem.Task;
                    break;
                case "action":
                    var actionItem = new ClosureWorkItem<Exception>(
                        static failure => throw failure, expected, null, _rootContext);
                    workItem = actionItem;
                    completion = actionItem.Task;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            _rootContext.Scheduler.QueueWorkItem(workItem);
            try
            {
                if (delayedFailure)
                {
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                    Assert.False(completion.IsCompleted);
                    release.SetResult();
                }

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.Same(expected, exception);
                Assert.True(completion.IsFaulted);
                var next = new Task<int>(() => 42);
                _rootContext.Scheduler.QueueTask(next);
                Assert.Equal(42, await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            }
            finally
            {
                release.TrySetResult();
                await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            async Task<int> FailAsync()
            {
                started.SetResult();
                await release.Task;
                throw expected;
            }
        }

        [Theory]
        [InlineData("task", false)]
        [InlineData("task", true)]
        [InlineData("result", false)]
        [InlineData("result", true)]
        [InlineData("value-task", false)]
        [InlineData("value-task", true)]
        public async Task RunOrQueueTask_PropagatesInlineDelegateFailureThroughCompletionTask(string kind, bool cancellationFailure)
        {
            Exception expected = cancellationFailure
                ? new OperationCanceledException()
                : new InvalidOperationException("Inline delegate failed.");
            await _rootContext.QueueTask(async () =>
            {
                Assert.Same(_rootContext, RuntimeContext.Current);
                Task completion = kind switch
                {
                    "task" => _rootContext.RunOrQueueTask(() => throw expected),
                    "result" => _rootContext.RunOrQueueTaskResult<int>(() => throw expected),
                    "value-task" => _rootContext.RunOrQueueTask<Exception>(static failure => throw failure, expected).AsTask(),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };

                Assert.True(completion.IsFaulted);
                Assert.Same(expected, await Record.ExceptionAsync(() => completion));
            }).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Sched_SimpleFifoTest()
        {
            // 这个测试并不理想，因为即使调度完全失效，两个闭包被“同时”执行，
            // 测试也有一半的概率通过

            var n = 0;
            var completed = new TaskCompletionSource<bool>();
            // ReSharper disable AccessToModifiedClosure
            void item1() { n = n + 5; }
            void item2() { n = n * 3; completed.SetResult(true); }
            // ReSharper restore AccessToModifiedClosure
            _rootContext.Scheduler.QueueAction(item1);
            _rootContext.Scheduler.QueueAction(item2);

            // 等待完成
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // N 应为 15，因为两个任务应按顺序执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(15, n);
            _output.WriteLine("Test executed OK.");
        }

        [Fact]
        public async Task Sched_Task_TplFifoTest()
        {
            // 这个测试并不理想，因为即使调度完全失效，两个闭包被“同时”执行，
            // 测试也有一半的概率通过

            var n = 0;
            var gate = new SemaphoreSlim(0, 1);

            // ReSharper disable AccessToModifiedClosure
            var task1 = new Task(() => { gate.Wait(); n = n + 5; });
            var task2 = new Task(() => { n = n * 3; });
            // ReSharper restore AccessToModifiedClosure

            _rootContext.Scheduler.QueueTask(task1);
            _rootContext.Scheduler.QueueTask(task2);

            // 释放 task1，使其继续执行
            gate.Release();

            await Task.WhenAll(task1, task2).WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            // N 应为 15，因为两个任务应按顺序执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(15, n);
        }

        [Fact]
        public async Task Sched_Task_ClosureWorkItem_Wait()
        {
            const int NumTasks = 10;
            var workItemCompletionTimeout = TimeSpan.FromSeconds(10);

            var flags = new ManualResetEvent[NumTasks];
            for (var i = 0; i < NumTasks; i++)
            {
                flags[i] = new ManualResetEvent(false);
            }

            var tasks = new Task[NumTasks];
            for (var i = 0; i < NumTasks; i++)
            {
                var taskNum = i; // 捕获
                tasks[i] = new Task(() => { _output.WriteLine("Inside Task-" + taskNum); flags[taskNum].WaitOne(); });
            }

            var workItems = new Task[NumTasks];
            for (var i = 0; i < NumTasks; i++)
            {
                var taskNum = i; // 捕获
                workItems[i] = _rootContext.QueueAction(taskIndex =>
                {
                    _output.WriteLine("Inside ClosureWorkItem-" + taskIndex);
                    tasks[taskIndex].Start(TaskScheduler.Default);
#pragma warning disable xUnit1031 // 不要在测试方法中使用阻塞式任务操作。
                    tasks[taskIndex].Wait();
#pragma warning restore xUnit1031 // 不要在测试方法中使用阻塞式任务操作。
                }, taskNum);
            }

            foreach (var flag in flags) flag.Set();
            await Task.WhenAll(workItems).WaitAsync(workItemCompletionTimeout, TestContext.Current.CancellationToken);


            for (var i = 0; i < tasks.Length; i++)
            {
                Assert.False(tasks[i].IsFaulted, "Task.IsFaulted-" + i + " Exception=" + tasks[i].Exception);
                Assert.True(tasks[i].IsCompleted, "Task.IsCompleted-" + i);
            }
        }

        [Fact]
        public async Task Sched_Task_TaskWorkItem_CurrentScheduler()
        {
            var result0 = new TaskCompletionSource<bool>();
            var result1 = new TaskCompletionSource<bool>();

            Task t1 = null!;
            _rootContext.Scheduler.QueueAction(() =>
            {
                try
                {
                    _output.WriteLine("#0 - TaskWorkItem - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                        SynchronizationContext.Current!, TaskScheduler.Current!);
                    var taskScheduler = ((WorkItemGroup)_rootContext.Scheduler).TaskScheduler;
                    Assert.Equal(taskScheduler, TaskScheduler.Current); //

                    t1 = new Task(() =>
                    {
                        _output.WriteLine("#1 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                            SynchronizationContext.Current!, TaskScheduler.Current!);
                        var taskScheduler = ((WorkItemGroup)_rootContext.Scheduler).TaskScheduler;  // "TaskScheduler.Current #1"
                        Assert.Equal(taskScheduler, TaskScheduler.Current); //
                        result1.SetResult(true);
                    });
                    t1.Start();

                    result0.SetResult(true);
                }
                catch (Exception exc)
                {
                    result0.SetException(exc);
                }
            });

            await result0.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
            Assert.True(result0.Task.Exception == null, "Task-0 should not throw exception: " + result0.Task.Exception);
            Assert.True(await result0.Task, "Task-0 completed");

            Assert.NotNull(t1); // 任务 1 已启动
            await result1.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
            // 设置 result0 后再给一点让出执行权的机会，因为 t1 任务可能尚未结束
            await t1.WaitAsync(TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

            Assert.True(t1.IsCompleted, "Task-1 completed");
            Assert.False(t1.IsFaulted, "Task-1 faulted: " + t1.Exception);
            Assert.True(await result1.Task, "Task-1 completed");
        }

        [Fact]
        public async Task Sched_Task_SubTaskExecutionSequencing()
        {
            LogContext("Main-task " + Task.CurrentId);

            var n = 0;
            var finished = new TaskCompletionSource<int>();
            var numCompleted = new[] { 0 };
            var gates = new SemaphoreSlim[10];
            for (var i = 0; i < 10; i++)
            {
                gates[i] = new SemaphoreSlim(0, 1);
            }

            void closure()
            {
                LogContext("ClosureWorkItem-task " + Task.CurrentId);

                for (var i = 0; i < 10; i++)
                {
                    var taskIndex = i;
                    var id = -1;
                    void action()
                    {
                        id = Task.CurrentId.HasValue ? (int)Task.CurrentId : -1;

                        // ReSharper disable AccessToModifiedClosure
                        LogContext("Sub-task " + id + " n=" + n);

                        var k = n;
                        _output.WriteLine("Sub-task " + id + " waiting for gate");
                        gates[taskIndex].Wait();
                        _output.WriteLine("Sub-task " + id + " proceeding");
                        n = k + 1;
                        // ReSharper restore AccessToModifiedClosure
                    }
                    Task.Factory.StartNew(action).ContinueWith(tsk =>
                    {
                        LogContext("Sub-task " + id + "-ContinueWith");

                        _output.WriteLine("Sub-task " + id + " Done");
                        if (Interlocked.Increment(ref numCompleted[0]) == 10)
                        {
                            finished.SetResult(0);
                        }
                    });
                }

                // 依次释放所有同步闸门，以确保顺序执行
                for (var i = 0; i < 10; i++)
                {
                    gates[i].Release();
                }
            }

            _rootContext.Scheduler.QueueAction(closure);

            // 等待完成
            _output.WriteLine("Main-task waiting");
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            _output.WriteLine("Main-task done");

            // N 应为 10，因为所有任务应串行执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(10, n);  // "Work items executed concurrently"
        }

        [Fact]
        public async Task Sched_AC_RequestContext_StartNew_ContinueWith()
        {
            const string key = "A";
            var val = Random.Shared.Next();
            RequestContext.Set(key, val);

            _output.WriteLine("Initial - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                SynchronizationContext.Current!, TaskScheduler.Current!);

            Assert.Equal(val, RequestContext.Get(key));  // "RequestContext.Get Initial"

            Task t0 = Task.Factory.StartNew(async () =>
            {
                _output.WriteLine("#0 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                    SynchronizationContext.Current!, TaskScheduler.Current!);

                Assert.Equal(val, RequestContext.Get(key));  // "RequestContext.Get #0"

                Task t1 = Task.Factory.StartNew(() =>
                {
                    _output.WriteLine("#1 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                        SynchronizationContext.Current!, TaskScheduler.Current!);
                    Assert.Equal(val, RequestContext.Get(key));  // "RequestContext.Get #1"
                });
                Task t2 = t1.ContinueWith((_) =>
                {
                    _output.WriteLine("#2 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                        SynchronizationContext.Current!, TaskScheduler.Current!);
                    Assert.Equal(val, RequestContext.Get(key));  // "RequestContext.Get #2"
                });
                await t2.WaitAsync(TimeSpan.FromSeconds(5));
            }).Unwrap();
            await t0.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(t0.IsCompleted, "Task #0 FAULTED=" + t0.Exception);
        }

        [Fact]
        public async Task RequestContextProtectedInQueuedTasksTest()
        {
            var key = Guid.NewGuid().ToString();
            var value = Guid.NewGuid().ToString();

            // QueueTask 内的清除操作不会影响调用方的 RequestContext
            RequestContext.Set(key, value);
            await _rootContext.QueueTask(() => AsyncCheckClearRequestContext(key));
            Assert.Equal(value, (string)RequestContext.Get(key)!);

            // 即使工作实际并非异步执行，QueueTask 内的清除操作也不会影响调用方的 RequestContext。
            await _rootContext.QueueTask(() => NonAsyncCheckClearRequestContext(key));
            Assert.Equal(value, (string)RequestContext.Get(key)!);

            // 工作异步执行时，调用方的 RequestContext 不受清除操作影响。
            async Task asyncCheckClearRequestContext()
            {
                RequestContext.Clear();
                Assert.Null(RequestContext.Get(key));
                await Task.Delay(TimeSpan.Zero);
            }
            await asyncCheckClearRequestContext();
            Assert.Equal(value, (string)RequestContext.Get(key)!);

            // 工作非异步执行时，调用方的 RequestContext 不受保护，可能被清除。
            Task nonAsyncCheckClearRequestContext()
            {
                RequestContext.Clear();
                Assert.Null(RequestContext.Get(key));
                return Task.CompletedTask;
            }
            await nonAsyncCheckClearRequestContext();
            Assert.Null(RequestContext.Get(key));

            static async Task AsyncCheckClearRequestContext(string key)
            {
                Assert.Null(RequestContext.Get(key));
                await Task.Delay(TimeSpan.Zero);
            }

            static Task NonAsyncCheckClearRequestContext(string key)
            {
                Assert.Null(RequestContext.Get(key));
                return Task.CompletedTask;
            }
        }

        private void LogContext(string what)
        {
            lock (Lockable)
            {
                _output.WriteLine(
                    "{0}\n"
                    + " TaskScheduler.Current={1}\n"
                    + " Task.Factory.Scheduler={2}\n"
                    + " SynchronizationContext.Current={3}\n"
                    + " Orleans-RuntimeContext.Current={4}",
                    what,
                    (TaskScheduler.Current == null ? "null" : TaskScheduler.Current.ToString())!,
                    (Task.Factory.Scheduler == null ? "null" : Task.Factory.Scheduler.ToString())!,
                    (SynchronizationContext.Current == null ? "null" : SynchronizationContext.Current.ToString())!,
                    (RuntimeContext.Current == null ? "null" : RuntimeContext.Current.ToString())!
                );

                //var st = new StackTrace();
                //output.WriteLine("Backtrace: " + st);
            }
        }

        internal static ILoggerFactory InitSchedulerLogging()
        {
            var filters = new LoggerFilterOptions();
            filters.AddFilter("Scheduler", LogLevel.Trace);
            filters.AddFilter("Scheduler.WorkerPoolThread", LogLevel.Trace);
            var loggerFactory = TestingUtils.CreateDefaultLoggerFactory(TestingUtils.CreateTraceFileName("Silo", DateTime.UtcNow.ToString("yyyyMMdd_hhmmss")), filters);
            return loggerFactory;
        }
    }
}

// ReSharper restore ConvertToConstant.Local

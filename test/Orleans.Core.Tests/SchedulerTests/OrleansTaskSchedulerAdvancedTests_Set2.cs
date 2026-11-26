using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Runtime.Scheduler;
using Orleans.Runtime.TestHooks;
using Orleans.Statistics;
using TestExtensions;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.SchedulerTests
{
    /// <summary>
    /// Orleans 任务调度器第二套高级测试。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class OrleansTaskSchedulerAdvancedTests_Set2 : IDisposable
    {
        private static readonly object Lockable = new object();
        private static readonly int WaitFactor = Debugger.IsAttached ? 100 : 1;
        private readonly ITestOutputHelper output;
        private readonly UnitTestSchedulingContext context;
        private readonly WorkItemGroup workItemGroup;
        private readonly ActivationTaskScheduler scheduler;
        private readonly IEnvironmentStatisticsProvider environmentStatisticsProvider;
        private readonly ILoggerFactory loggerFactory;
        public OrleansTaskSchedulerAdvancedTests_Set2(ITestOutputHelper output)
        {
            this.output = output;
            this.loggerFactory = OrleansTaskSchedulerBasicTests.InitSchedulerLogging();
            this.context = UnitTestSchedulingContext.Create(loggerFactory);
            this.workItemGroup = context.WorkItemGroup;
            this.scheduler = workItemGroup.TaskScheduler;
            this.environmentStatisticsProvider = new TestHooksEnvironmentStatisticsProvider();
        }

        public void Dispose()
        {
            this.loggerFactory.Dispose();
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_SimpleFifoTest()
        {
            // 这不是一个伟大的测试 因为有一个50/50的镜头 即使时间安排,它也会有效
            // 封锁完全彻底被打破,

            int n = 0;
            var gate = new SemaphoreSlim(0, 1);
            // ReSharper disable AccessToModifiedClosure
            var task1 = new Task(() => { gate.Wait(); n = n + 5; });
            Task task2 = new Task(() => { n = n * 3; });
            // ReSharper restore AccessToModifiedClosure

            task1.Start(scheduler);
            task2.Start(scheduler);

            // 释放大门,让任务1得以继续
            gate.Release();

            await task1;
            await task2;

            // N应为15岁,因为这两项任务应执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(15, n);  // “工作项目执行不合秩序”
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_NewTask_ContinueWith_Wrapped()
        {
            Task<Task> wrapped = new Task<Task>(() =>
            {
                this.output.WriteLine("#0 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                    SynchronizationContext.Current!, TaskScheduler.Current!);

                Task t0 = new Task(() =>
                {
                    this.output.WriteLine("#1 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                        SynchronizationContext.Current!, TaskScheduler.Current!);
                    Assert.Equal(scheduler, TaskScheduler.Current);  // "TaskScheduler.Current #1"
                });
                Task t1 = t0.ContinueWith(task =>
                {
                    Assert.False(task.IsFaulted, "Task #1 Faulted=" + task.Exception);

                    this.output.WriteLine("#2 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                        SynchronizationContext.Current!, TaskScheduler.Current!);
                    Assert.Equal(scheduler, TaskScheduler.Current);  // "TaskScheduler.Current #2"
                });
                t0.Start(scheduler);
                return t1;
            });
            wrapped.Start(scheduler);
            await wrapped.Unwrap().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_SubTaskExecutionSequencing()
        {
            LogContext("Main-task " + Task.CurrentId);

            int n = 0;

            var finished = new TaskCompletionSource();
            int numCompleted = 0;
            var gates = new SemaphoreSlim[10];
            for (int i = 0; i < 10; i++)
            {
                gates[i] = new SemaphoreSlim(0, 1);
            }

            void action()
            {
                LogContext("WorkItem-task " + Task.CurrentId);

                for (int i = 0; i < 10; i++)
                {
                    int taskIndex = i;
                    int id = -1;
                    Task.Factory.StartNew(() =>
                    {
                        id = Task.CurrentId.HasValue ? (int)Task.CurrentId : -1;

                        // ReSharper disable AccessToModifiedClosure
                        LogContext("Sub-task " + id + " n=" + n);
                        int k = n;
                        this.output.WriteLine("Sub-task " + id + " waiting for gate");
                        gates[taskIndex].Wait();
                        this.output.WriteLine("Sub-task " + id + " awake");
                        n = k + 1;
                        // ReSharper restore AccessToModifiedClosure
                    })
                    .ContinueWith(tsk =>
                    {
                        LogContext("Sub-task " + id + "-ContinueWith");

                        this.output.WriteLine("Sub-task " + id + " Done");
                        if (Interlocked.Increment(ref numCompleted) == 10)
                        {
                            finished.SetResult();
                        }
                    });
                }

                // 释放所有门,按顺序排列,以确保相继执行
                for (int i = 0; i < 10; i++)
                {
                    gates[i].Release();
                }
            }

            Task t = new Task(action);

            t.Start(scheduler);

            // 等待完成
            this.output.WriteLine("Main-task waiting");
            await finished.Task;
            this.output.WriteLine("Main-task done");

            // N应为10,因为所有任务都应连续执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(10, n);  // “同时执行的工作项目”
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_ContinueWith_1_Test()
        {
            var result = new TaskCompletionSource<bool>();
            int n = 0;
            var gate = new SemaphoreSlim(0, 1);

            Task wrapper = new Task(() =>
            {
                // ReSharper disable AccessToModifiedClosure
                Task task1 = Task.Factory.StartNew(() => { this.output.WriteLine("===> 1a"); gate.Wait(); n = n + 3; this.output.WriteLine("===> 1b"); });
                Task task2 = task1.ContinueWith(task => { n = n * 5; this.output.WriteLine("===> 2"); });
                Task task3 = task2.ContinueWith(task => { n = n / 5; this.output.WriteLine("===> 3"); });
                Task task4 = task3.ContinueWith(task => { n = n - 2; this.output.WriteLine("===> 4"); result.SetResult(true); });
                // ReSharper restore AccessToModifiedClosure
                task4.ContinueWith(task =>
                {
                    this.output.WriteLine("Done Faulted={0}", task.IsFaulted);
                    Assert.False(task.IsFaulted, "Faulted with Exception=" + task.Exception);
                });

                // 释放大门, 允许任务1完成
                gate.Release();
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await result.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(1, n);  // “工作项目执行不合秩序”
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_WhenAny()
        {
            ManualResetEvent pause1 = new ManualResetEvent(false);
            ManualResetEvent pause2 = new ManualResetEvent(false);
            var finish = new TaskCompletionSource<bool>();
            Task<int> task1 = null!;
            Task<int> task2 = null!;
            Task join = null!;
            Task wrapper = new Task(() =>
            {
                task1 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-1 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);  // "TaskScheduler.Current=" + TaskScheduler.Current
                    pause1.WaitOne();
                    this.output.WriteLine("Task-1 Done");
                    return 1;
                });
                task2 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-2 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    pause2.WaitOne();
                    this.output.WriteLine("Task-2 Done");
                    return 2;
                });

                join = Task.WhenAny(task1, task2);

                finish.SetResult(true);
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await finish.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            pause1.Set();
            await join.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.True(join.IsCompleted && !join.IsFaulted, "Join Status " + join.Status);
            Assert.False(task1.IsFaulted, "Task-1 Faulted " + task1.Exception);
            Assert.False(task2.IsFaulted, "Task-2 Faulted " + task2.Exception);
            Assert.True(task1.IsCompleted || task2.IsCompleted, "Task-1 Status = " + task1.Status + " Task-2 Status = " + task2.Status);
            pause2.Set();
            task2.Ignore();
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_WhenAny_Timeout()
        {
            ManualResetEvent pause1 = new ManualResetEvent(false);
            ManualResetEvent pause2 = new ManualResetEvent(false);
            var finish = new TaskCompletionSource<bool>();
            var fakeTimeProvider = new FakeTimeProvider();
            Task<int> task1 = null!;
            Task<int> task2 = null!;
            Task join = null!;
            Task wrapper = new Task(() =>
            {
                task1 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-1 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    pause1.WaitOne();
                    this.output.WriteLine("Task-1 Done");
                    return 1;
                });
                task2 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-2 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    pause2.WaitOne();
                    this.output.WriteLine("Task-2 Done");
                    return 2;
                });

                join = Task.WhenAny(task1, task2, Task.Delay(TimeSpan.FromSeconds(2), fakeTimeProvider, TestContext.Current.CancellationToken));

                finish.SetResult(true);
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await finish.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            // 触发延迟超时的提前时间
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));

            Assert.NotNull(join);
            await join;
            Assert.True(join.IsCompleted && !join.IsFaulted, "Join Status " + join.Status);
            Assert.False(task1.IsFaulted, "Task-1 Faulted " + task1.Exception);
            Assert.False(task1.IsCompleted, "Task-1 Status " + task1.Status);
            Assert.False(task2.IsFaulted, "Task-2 Faulted " + task2.Exception);
            Assert.False(task2.IsCompleted, "Task-2 Status " + task2.Status);
            pause1.Set();
            task1.Ignore();
            pause2.Set();
            task2.Ignore();
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_WhenAny_Busy_Timeout()
        {
            var pause1 = new TaskCompletionSource<bool>();
            var pause2 = new TaskCompletionSource<bool>();
            var finish = new TaskCompletionSource<bool>();
            var fakeTimeProvider = new FakeTimeProvider();
            Task<int> task1 = null!;
            Task<int> task2 = null!;
            Task join = null!;
            Task wrapper = new Task(() =>
            {
                task1 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-1 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    int num1 = 1;
                    pause1.Task.Wait();
                    this.output.WriteLine("Task-1 Done");
                    return num1;
                });
                task2 = Task<int>.Factory.StartNew(() =>
                {
                    this.output.WriteLine("Task-2 Started");
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    int num2 = 2;
                    pause2.Task.Wait();
                    this.output.WriteLine("Task-2 Done");
                    return num2;
                });

                var delayTask = Task.Delay(TimeSpan.FromSeconds(2), fakeTimeProvider);

                // 立即提前时间,以触发延误
                fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));

                join = Task.WhenAny(task1, task2, delayTask);

                finish.SetResult(true);
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await finish.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            Assert.NotNull(join); // 指定加入承诺
            await join;
            Assert.True(join.IsCompleted && !join.IsFaulted, "Join Status " + join.Status);
            Assert.False(task1.IsFaulted, "Task-1 Faulted " + task1.Exception);
            Assert.False(task1.IsCompleted, "Task-1 Status " + task1.Status);
            Assert.False(task2.IsFaulted, "Task-2 Faulted " + task2.Exception);
            Assert.False(task2.IsCompleted, "Task-2 Status " + task2.Status);
        }


        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Task_Run()
        {
            ManualResetEvent pause1 = new ManualResetEvent(false);
            ManualResetEvent pause2 = new ManualResetEvent(false);
            var finish = new TaskCompletionSource<bool>();
            Task<int> task1 = null!;
            Task<int> task2 = null!;
            Task join = null!;
            Task wrapper = new Task(() =>
            {
                task1 = Task.Run(() =>
                {
                    this.output.WriteLine("Task-1 Started");
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    pause1.WaitOne();
                    this.output.WriteLine("Task-1 Done");
                    return 1;
                });
                task2 = Task.Run(() =>
                {
                    this.output.WriteLine("Task-2 Started");
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    pause2.WaitOne();
                    this.output.WriteLine("Task-2 Done");
                    return 2;
                });

                join = Task.WhenAll(task1, task2).ContinueWith(t =>
                {
                    this.output.WriteLine("Join Started");
                    if (t.IsFaulted) throw t.Exception;
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    this.output.WriteLine("Join Done");
                });

                finish.SetResult(true);
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await finish.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            pause1.Set();
            pause2.Set();
            Assert.NotNull(join); // 指定加入承诺
            await join;
            Assert.True(join.IsCompleted && !join.IsFaulted, "Join Status " + join);
            Assert.True(task1.IsCompleted && !task1.IsFaulted, "Task-1 Status " + task1);
            Assert.True(task2.IsCompleted && !task2.IsFaulted, "Task-2 Status " + task2);
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Task_Run_Delay()
        {
            ManualResetEvent pause1 = new ManualResetEvent(false);
            ManualResetEvent pause2 = new ManualResetEvent(false);
            var finish = new TaskCompletionSource<bool>();
            Task<int> task1 = null!;
            Task<int> task2 = null!;
            Task join = null!;
            Task wrapper = new Task(() =>
            {
                task1 = Task.Run(async () =>
                {
                    this.output.WriteLine("Task-1 Started");
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    await Task.Delay(1);
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    pause1.WaitOne();
                    this.output.WriteLine("Task-1 Done");
                    return 1;
                });
                task2 = Task.Run(async () =>
                {
                    this.output.WriteLine("Task-2 Started");
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    await Task.Delay(1);
                    Assert.NotEqual(scheduler, TaskScheduler.Current);
                    pause2.WaitOne();
                    this.output.WriteLine("Task-2 Done");
                    return 2;
                });

                join = Task.WhenAll(task1, task2).ContinueWith(t =>
                {
                    this.output.WriteLine("Join Started");
                    if (t.IsFaulted) throw t.Exception;
                    Assert.Equal(scheduler, TaskScheduler.Current);
                    this.output.WriteLine("Join Done");
                });

                finish.SetResult(true);
            });
            wrapper.Start(scheduler);

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await finish.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            pause1.Set();
            pause2.Set();
            Assert.NotNull(join); // 指定加入承诺
            await join;
            Assert.True(join.IsCompleted && !join.IsFaulted, "Join Status " + join);
            Assert.True(task1.IsCompleted && !task1.IsFaulted, "Task-1 Status " + task1);
            Assert.True(task2.IsCompleted && !task2.IsFaulted, "Task-2 Status " + task2);
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Task_Delay()
        {
            Task<Task> wrapper = new Task<Task>(async () =>
            {
                Assert.Equal(scheduler, TaskScheduler.Current);
                await DoDelay(1, TestContext.Current.CancellationToken);
                Assert.Equal(scheduler, TaskScheduler.Current);
                await DoDelay(2, TestContext.Current.CancellationToken);
                Assert.Equal(scheduler, TaskScheduler.Current);
            });
            wrapper.Start(scheduler);

            await wrapper.Unwrap();
        }

        private async Task DoDelay(int i, CancellationToken cancellationToken)
        {
            try
            {
                this.output.WriteLine("Before Task.Delay #{0} TaskScheduler.Current={1}", i, TaskScheduler.Current);
                await Task.Delay(1, cancellationToken);
                this.output.WriteLine("After Task.Delay #{0} TaskScheduler.Current={1}", i, TaskScheduler.Current);
            }
            catch (ObjectDisposedException)
            {
                // 忽略任何问题 ObjectDisposedException 如果控制台输出流已经关闭
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Turn_Execution_Order_Loop()
        {
            const int NumChains = 10;
            const int ChainLength = 3;
            // 能否加上一个单位测试,基本检查任何转弯是否真的在任何其他转弯之前一直运行到完成?
            // 例如,你有一个长长的运行主转弯,中间它产生许多短的(承诺)和 StartNew。
            // 您测试在主转弯完全完成之前没有 CW/StartNew 运行。 然后按压力运行 。

            var resultHandles = new TaskCompletionSource<bool>[NumChains];
            Task[] taskChains = new Task[NumChains];
            Task[] taskChainEnds = new Task[NumChains];
            bool[] executingChain = new bool[NumChains];
            int[] stageComplete = new int[NumChains];
            int executingGlobal = -1;
            var gates = new ManualResetEventSlim[NumChains];

            for (int i = 0; i < NumChains; i++)
            {
                gates[i] = new ManualResetEventSlim(false);
                int chainNum = i; // 捕获
                resultHandles[chainNum] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task task = taskChains[chainNum] = new Task(() =>
                {
                    const int taskNum = 0;
                    try
                    {
                        Assert.Equal(-1, executingGlobal);  // " 在链条中检测到的意外其他执行 " + chainNum + " 任务 " + taskNum
                        Assert.False(executingChain[chainNum], "Detected unexpected other execution on chain " + chainNum + " Task " + taskNum);

                        executingGlobal = chainNum;
                        executingChain[chainNum] = true;

                        gates[chainNum].Wait();
                    }
                    finally
                    {
                        stageComplete[chainNum] = taskNum;
                        executingChain[chainNum] = false;
                        executingGlobal = -1;
                    }
                });
                for (int j = 1; j < ChainLength; j++)
                {
                    int taskNum = j; // 捕获
                    task = task.ContinueWith(t =>
                    {
                        if (t.IsFaulted) throw t.Exception;
                        this.output.WriteLine("Inside Chain {0} Task {1}", chainNum, taskNum);
                        try
                        {
                            Assert.Equal(-1, executingGlobal);  // " 在链条中检测到的意外其他执行 " + chainNum + " 任务 " + taskNum
                            Assert.False(executingChain[chainNum], "Detected unexpected other execution on chain " + chainNum + " Task " + taskNum);
                            Assert.Equal(taskNum - 1, stageComplete[chainNum]);  // " 链上检测到的意外执行阶段 " + chainNum + " 任务 " + taskNum

                            executingGlobal = chainNum;
                            executingChain[chainNum] = true;

                            gates[chainNum].Wait();
                        }
                        finally
                        {
                            stageComplete[chainNum] = taskNum;
                            executingChain[chainNum] = false;
                            executingGlobal = -1;
                        }
                    }, scheduler);
                }
                taskChainEnds[chainNum] = task.ContinueWith(t =>
                {
                    if (t.IsFaulted) throw t.Exception;
                    this.output.WriteLine("Inside Chain {0} Final Task", chainNum);
                    resultHandles[chainNum].SetResult(true);
                }, scheduler);
            }

            for (int i = 0; i < NumChains; i++)
            {
                taskChains[i].Start(scheduler);
            }

            // 设置所有允许执行行刑的大门
            for (int i = 0; i < NumChains; i++)
            {
                gates[i].Set();
            }

            for (int i = 0; i < NumChains; i++)
            {
                TimeSpan waitCheckTime = TimeSpan.FromMilliseconds(1_500 * ChainLength * NumChains * WaitFactor);

                try
                {
                    await resultHandles[i].Task.WaitAsync(waitCheckTime, TestContext.Current.CancellationToken);
                }
                catch (TimeoutException)
                {
                    Assert.Fail("Result did not arrive before timeout " + waitCheckTime);
                }

                bool ok = await resultHandles[i].Task;

                try
                {
                    // 之后 resultHandle 完成并不直接意味着最后的连锁已经完成(有种族状况的可能),
                    await taskChainEnds[i].WaitAsync(
                        TimeSpan.FromMilliseconds(10),
                        TestContext.Current.CancellationToken);
                }
                catch (TimeoutException)
                {
                    Assert.Fail($"Task chain end {i} should complete very shortly after after its resultHandle");
                }

                Assert.True(taskChainEnds[i].IsCompleted, "Task chain " + i + " should be completed");
                Assert.False(taskChainEnds[i].IsFaulted, "Task chain " + i + " should not be Faulted: " + taskChainEnds[i].Exception);
                Assert.Equal(ChainLength - 1, stageComplete[i]);  // " 工作链 " +i+ " 应已完成所有阶段"
                Assert.True(ok, "Successfully waited for ResultHandle for Task chain " + i);
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Test1()
        {
            await Run_ActivationSched_Test1(scheduler, false, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task ActivationSched_Test1_Bounce()
        {
            await Run_ActivationSched_Test1(scheduler, true, TestContext.Current.CancellationToken);
        }

        [Theory, TestCategory("Functional"), TestCategory("Scheduler")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ActivationSched_Test1_FromDisposedLoggerContext(bool bounceToThreadPool)
        {
            using var previousLoggerFactory = new LoggerFactory();
            using var previousContext = UnitTestSchedulingContext.Create(previousLoggerFactory);
            previousLoggerFactory.Dispose();
            Assert.Throws<ObjectDisposedException>(() => previousLoggerFactory.CreateLogger("Disposed"));

            Task run;
            RuntimeContext.SetExecutionContext(previousContext, out var originalContext);
            try
            {
                run = Run_ActivationSched_Test1(scheduler, bounceToThreadPool, TestContext.Current.CancellationToken);
            }
            finally
            {
                // RuntimeContext 线是本地的,所以在等待之前把它还原。
                RuntimeContext.ResetExecutionContext(originalContext);
            }

            await run;
        }

        [Theory, TestCategory("Functional"), TestCategory("Scheduler")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ActivationSched_Test1_WithDisposedLoggerFactory(bool bounceToThreadPool)
        {
            loggerFactory.Dispose();

            var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                Run_ActivationSched_Test1(scheduler, bounceToThreadPool, TestContext.Current.CancellationToken));

            Assert.Equal(nameof(LoggerFactory), exception.ObjectName);
        }

        internal async Task Run_ActivationSched_Test1(
            TaskScheduler scheduler,
            bool bounceToThreadPool,
            CancellationToken cancellationToken)
        {
            var grainId = LegacyGrainId.GetGrainId(0, Guid.NewGuid());
            var silo = new MockSiloDetails
            {
                SiloAddress = SiloAddressUtils.NewLocalSiloAddress(23)
            };
            var grain = await Task.Factory.StartNew(async () =>
            {
                var activation = new NonReentrantStressGrainWithoutState();
                Assert.Same(context, activation.GrainContext);
                await activation.OnActivateAsync(CancellationToken.None);
                return activation;
            }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();

            Task wrapped = null!;
            var wrapperDone = new TaskCompletionSource<bool>();
            var wrappedDone = new TaskCompletionSource<bool>();
            Task<Task> wrapper = new Task<Task>(() =>
            {
                this.output.WriteLine("#0 - new Task - SynchronizationContext.Current={0} TaskScheduler.Current={1}",
                    SynchronizationContext.Current!, TaskScheduler.Current!);

                Task t1 = grain.Test1();

                void wrappedDoneAction(Task task)
                {
                    task.GetAwaiter().GetResult();
                    wrappedDone.SetResult(true);
                }

                if (bounceToThreadPool)
                {
                    wrapped = t1.ContinueWith(wrappedDoneAction,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else
                {
                    wrapped = t1.ContinueWith(wrappedDoneAction);
                }
                wrapperDone.SetResult(true);
                return wrapped;
            });
            wrapper.Start(scheduler);
            await wrapper;

            var timeoutLimit = TimeSpan.FromSeconds(10);
            try
            {
                await wrapperDone.Task.WaitAsync(timeoutLimit, cancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }
            bool done = wrapperDone.Task.Result;

            Assert.True(done, "Wrapper Task finished");
            Assert.True(wrapper.IsCompleted, "Wrapper Task completed");

            //完成= wrapped.Wait(TimeSpan.FromSeconds(12));
            //Assert.True(done, "Wrapped Task not timeout");
            await wrapped;
            try
            {
                await wrappedDone.Task.WaitAsync(timeoutLimit, cancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }
            done = wrappedDone.Task.Result;
            Assert.True(done, "Wrapped Task should be finished");
            Assert.True(wrapped.IsCompleted, "Wrapped Task completed");
        }

        private void LogContext(string what)
        {
            lock (Lockable)
            {
                this.output.WriteLine(
                    "{0}\n"
                    + " TaskScheduler.Current={1}\n"
                    + " Task.Factory.Scheduler={2}\n"
                    + " SynchronizationContext.Current={3}",
                    what,
                    (TaskScheduler.Current == null ? "null" : TaskScheduler.Current.ToString())!,
                    (Task.Factory.Scheduler == null ? "null" : Task.Factory.Scheduler.ToString())!,
                    (SynchronizationContext.Current == null ? "null" : SynchronizationContext.Current.ToString())!
                );

                //var st = new StackTrace();
                //output.WriteLine(st.ToString());
            }
        }

        private class MockSiloDetails : ILocalSiloDetails
        {
            public string DnsHostName { get; } = null!;
            public SiloAddress SiloAddress { get; set; } = null!;
            public SiloAddress GatewayAddress { get; } = null!;
            public string Name { get; set; } = Guid.NewGuid().ToString();
            public string ClusterId { get; } = null!;
        }
    }
}

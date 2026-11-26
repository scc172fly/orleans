using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Scheduler;
using Orleans.Internal;
using Xunit;

namespace UnitTests.SchedulerTests
{
    /// <summary>
    /// 高级测试 Orleans 任务调度器功能。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class OrleansTaskSchedulerAdvancedTests(ITestOutputHelper output) : IDisposable
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
        private readonly ILoggerFactory _loggerFactory = OrleansTaskSchedulerBasicTests.InitSchedulerLogging();

        public void Dispose()
        {
            _loggerFactory.Dispose();
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_Test()
        {
            var n = 0;
            var insideTask = false;
            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            output.WriteLine("Running Main in Context=" + RuntimeContext.Current);
            var tasksTask = new TaskCompletionSource<List<Task>>();
            var gates = new SemaphoreSlim[10];
            for (var i = 0; i < 10; i++)
            {
                gates[i] = new SemaphoreSlim(0, 1);
            }

            context.Scheduler.QueueAction(() =>
                {
                    var tasks = new List<Task>(10);
                    for (var i = 0; i < 10; i++)
                    {
                        var taskNum = i;
                        tasks.Add(Task.Factory.StartNew(() =>
                        {
                            output.WriteLine("Starting " + taskNum + " in Context=" + RuntimeContext.Current);
                            Assert.False(insideTask, $"Starting new task when I am already inside task of iteration {n}");
                            insideTask = true;

                            // 如果其中两项任务同时进行,则会加速数据竞赛的可能性。
                            var k = n;
                            gates[taskNum].Wait();
                            n = k + 1;

                            insideTask = false;
                        },
                        CancellationToken.None,
                        TaskCreationOptions.None,
                        TaskScheduler.Current));
                    }
                    tasksTask.SetResult(tasks);

                    // 释放所有门,按顺序排列,以确保相继执行
                    for (var i = 0; i < 10; i++)
                    {
                        gates[i].Release();
                    }
                });

            await Task.WhenAll(await tasksTask.Task);

            // N应为10,因为所有任务都应连续执行
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(10, n);  // “同时执行的工作项目”
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_WaitTest()
        {
            var n = 0;
            var insideTask = false;
            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            var result = new TaskCompletionSource<bool>();
            var gate = new SemaphoreSlim(0, 1);

            context.Scheduler.QueueAction(() =>
                {
                    var task1 = Task.Factory.StartNew(() =>
                    {
                        output.WriteLine("Starting 1");
                        Assert.False(insideTask, $"Starting new task when I am already inside task of iteration {n}");
                        insideTask = true;
                        output.WriteLine("===> 1a");
                        gate.Wait();
                        n = n + 3;
                        output.WriteLine("===> 1b");
                        insideTask = false;
                    });
                    var task2 = Task.Factory.StartNew(() =>
                    {
                        output.WriteLine("Starting 2");
                        Assert.False(insideTask, $"Starting new task when I am already inside task of iteration {n}");
                        insideTask = true;
                        output.WriteLine("===> 2a");
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
                        task1.Wait();
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
                        output.WriteLine("===> 2b");
                        n = n * 5;
                        output.WriteLine("===> 2c");
                        insideTask = false;
                        result.SetResult(true);
                    });
                    task1.Ignore();
                    task2.Ignore();

                    // 释放大门,让任务1 完成
                    gate.Release();
                });

            var timeoutLimit = TimeSpan.FromMilliseconds(3000);
            try
            {
                await result.Task.WaitAsync(timeoutLimit, TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Result did not arrive before timeout " + timeoutLimit);
            }

            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(15, n);  // “工作项目执行不合秩序”
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_Turn_Execution_Order()
        {
            // 能否加上一个单位测试,基本检查任何转弯是否真的在任何其他转弯之前一直运行到完成?
            // 例如,你有一个长长的运行主转弯,中间它产生许多短的(承诺)和 StartNew。
            // 您测试在主转弯完全完成之前没有 CW/StartNew 运行。 然后按压力运行 。

            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            var result1 = new TaskCompletionSource<bool>();
            var result2 = new TaskCompletionSource<bool>();
            var mainTurnGate = new TaskCompletionSource<bool>();
            var mainDone = false;
            var stageNum1 = 0;
            var stageNum2 = 0;

            context.Scheduler.QueueAction(() =>
            {
                mainDone = false;
                stageNum1 = stageNum2 = 0;

                var task1 = Task.Factory.StartNew(() => SubProcess1(11));
                var task2 = task1.ContinueWith((_) => SubProcess1(12));
                var task3 = task2.ContinueWith((_) => SubProcess1(13));
                var task4 = task3.ContinueWith((_) => { SubProcess1(14); result1.SetResult(true); });
                task4.Ignore();

                var task21 = Task.CompletedTask.ContinueWith((_) => SubProcess2(21));
                var task22 = task21.ContinueWith((_) => { SubProcess2(22); result2.SetResult(true); });
                task22.Ignore();

                // 等待门的开关确保定点
                mainTurnGate.Task.Wait();
                mainDone = true;
            });

            // 释放大门,让主转弯完成
            mainTurnGate.SetResult(true);

            try { await result1.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken); }
            catch (TimeoutException) { Assert.Fail("Timeout-1"); }
            try { await result2.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken); }
            catch (TimeoutException) { Assert.Fail("Timeout-2"); }

            Assert.NotEqual(0, stageNum1); // “工作项目没有被执行-1”
            Assert.NotEqual(0, stageNum2);  // “工作项目未执行 2”
            Assert.Equal(14, stageNum1);  // "工作项目执行 超出订单-1
            Assert.Equal(22, stageNum2);  // "工作项目 执行超出订单2"

            void SubProcess1(int n)
            {
                var msg = string.Format("1-{0} MainDone={1} inside Task {2}", n, mainDone, Task.CurrentId);
                output.WriteLine("1 ===> " + msg);
                Assert.True(mainDone, msg + " -- Main turn should be finished");
                stageNum1 = n;
            }

            void SubProcess2(int n)
            {
                var msg = string.Format("2-{0} MainDone={1} inside Task {2}", n, mainDone, Task.CurrentId);
                output.WriteLine("2 ===> " + msg);
                Assert.True(mainDone, msg + " -- Main turn should be finished");
                stageNum2 = n;
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_Stopped_WorkItemGroup()
        {
            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            void CheckScheduler(object? state)
            {
                Assert.IsType<string>(state);
                Assert.Equal("some state", state as string);
                Assert.IsType<ActivationTaskScheduler>(TaskScheduler.Current);
            }

            Task<Task> ScheduleTask() => Task.Factory.StartNew(
                state =>
                {
                    CheckScheduler(state);

                    return Task.Factory.StartNew(
                        async s =>
                        {
                            CheckScheduler(s);
                            await Task.Delay(50);
                            CheckScheduler(s);
                        },
                        state, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Current).Unwrap();
                },
                "some state",
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                context.WorkItemGroup.TaskScheduler);

            // 检查 WorkItemGroup 是否正常运行。
            await await ScheduleTask();

            var taskAfterStopped = ScheduleTask();
            var resultTask = await Task.WhenAny(
                taskAfterStopped,
                Task.Delay(WaitTimeout, TestContext.Current.CancellationToken));
            Assert.Same(taskAfterStopped, resultTask);

            await await taskAfterStopped;

            // 等待 WorkItemGroup 将警告升级为错误并重试。
            // 延误基于 SchedulingOptions.StoppedActivationWarningInterval。
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

            taskAfterStopped = ScheduleTask();
            resultTask = await Task.WhenAny(
                taskAfterStopped,
                Task.Delay(WaitTimeout, TestContext.Current.CancellationToken));
            Assert.Same(taskAfterStopped, resultTask);

            await await taskAfterStopped;
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_Task_Turn_Execution_Order()
        {
            // 检查任何转弯是否真的运行到完成的单位测试, 在任何其他转弯之前?
            // 例如,你有一个长长的运行主转弯,中间它产生许多短的(承诺)和 StartNew。
            // 您测试在主转弯完全完成之前没有 CW/StartNew 运行。 然后按压力运行 。

            var context = UnitTestSchedulingContext.Create(_loggerFactory);
            var activationScheduler = context.WorkItemGroup.TaskScheduler;

            var mainDone = false;
            var stageNum1 = 0;
            var stageNum2 = 0;

            var result1 = new TaskCompletionSource<bool>();
            var result2 = new TaskCompletionSource<bool>();
            var wrapperGate = new TaskCompletionSource<bool>();
            var mainTurnGate = new TaskCompletionSource<bool>();

            Task wrapper = null!;
            Task finalTask1 = null!;
            Task finalPromise2 = null!;
            var wrapperCreated = new TaskCompletionSource<Task>();

            context.Scheduler.QueueAction(() =>
            {
                Log(1, "Outer ClosureWorkItem " + Task.CurrentId + " starting");
                Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #0"

                Log(2, "Starting wrapper Task");
                wrapper = Task.Factory.StartNew(() =>
                {
                    Log(3, "Inside wrapper Task Id=" + Task.CurrentId);
                    Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #1"

                    // 执行链 #1
                    Log(4, "Wrapper Task Id=" + Task.CurrentId + " creating Task chain");
                    var task1 = Task.Factory.StartNew(() =>
                    {
                        Log(5, "#11 Inside sub-Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #11"
                        SubProcess1(11);
                    });
                    var task2 = task1.ContinueWith((Task task) =>
                    {
                        Log(6, "#12 Inside continuation Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #12"
                        if (task.IsFaulted) throw task.Exception.Flatten();
                        SubProcess1(12);
                    });
                    var task3 = task2.ContinueWith(task =>
                    {
                        Log(7, "#13 Inside continuation Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #13"
                        if (task.IsFaulted) throw task.Exception.Flatten();
                        SubProcess1(13);
                    });
                    finalTask1 = task3.ContinueWith(task =>
                    {
                        Log(8, "#14 Inside final continuation Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #14"
                        if (task.IsFaulted) throw task.Exception.Flatten();
                        SubProcess1(14);
                        result1.SetResult(true);
                    });

                    // 执行链 #2
                    Log(9, "Wrapper Task " + Task.CurrentId + " creating AC chain");
                    var promise2 = Task.Factory.StartNew(() =>
                    {
                        Log(10, "#21 Inside sub-Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #21"
                        SubProcess2(21);
                    });
                    finalPromise2 = promise2.ContinueWith((_) =>
                    {
                        Log(11, "#22 Inside final continuation Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #22"
                        SubProcess2(22);
                        result2.SetResult(true);
                    });
                    finalPromise2.Ignore();

                    Log(12, "Wrapper Task Id=" + Task.CurrentId + " waiting for gate");
                    wrapperGate.Task.Wait();

                    Log(13, "Wrapper Task Id=" + Task.CurrentId + " finished");

                    void SubProcess1(int n)
                    {
                        var msg = string.Format("1-{0} MainDone={1} inside Task {2}", n, mainDone, Task.CurrentId);
                        output.WriteLine("1 ===> " + msg);
                        Assert.True(mainDone, msg + " -- Main turn should be finished");
                        stageNum1 = n;
                    }

                    void SubProcess2(int n)
                    {
                        var msg = string.Format("2-{0} MainDone={1} inside Task {2}", n, mainDone, Task.CurrentId);
                        output.WriteLine("2 ===> " + msg);
                        Assert.True(mainDone, msg + " -- Main turn should be finished");
                        stageNum2 = n;
                    }
                }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Current);

                wrapperCreated.SetResult(wrapper);

                Log(14, "Outer ClosureWorkItem Task Id=" + Task.CurrentId + " waiting for gate");
                mainTurnGate.Task.Wait();
                Log(15, "Outer ClosureWorkItem Task Id=" + Task.CurrentId + " continuing");

                Log(16, "Finished Outer ClosureWorkItem Task Id=" + wrapper.Id);
                mainDone = true;
            });

            Log(17, "Waiting for ClosureWorkItem to spawn wrapper Task");
            wrapper = await wrapperCreated.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.NotNull(wrapper); // 未创建任务

            // 释放大门,以便行刑
            wrapperGate.SetResult(true);
            mainTurnGate.SetResult(true);

            Log(18, "Waiting for wrapper Task Id=" + wrapper.Id + " to complete");
            await wrapper.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.False(wrapper.IsFaulted, "Wrapper Task faulted: " + wrapper.Exception);
            Assert.True(wrapper.IsCompleted, "Wrapper Task should be completed");

            Log(20, "Waiting for TaskWorkItem to complete");
            // 使用合理的超时等待 mainDone
            for (var i = 0; i < 100 && !mainDone; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Log(21, "Done waiting for TaskWorkItem to complete MainDone=" + mainDone);
            Assert.True(mainDone, "Main Task should be completed");
            Assert.NotNull(finalTask1); // 未创建任务链 # 1
            Assert.NotNull(finalPromise2); // 未创建任务链 # 2

            Log(22, "Waiting for final task #1 to complete");
            await finalTask1.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.False(finalTask1.IsFaulted, "Final Task faulted: " + finalTask1.Exception);
            Assert.True(finalTask1.IsCompleted, "Final Task completed");
            Assert.True(await result1.Task, "Timeout-1");

            Log(24, "Waiting for final promise #2 to complete");
            await finalPromise2.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Log(25, "Done waiting for final promise #2");
            Assert.False(finalPromise2.IsFaulted, "Final Task faulted: " + finalPromise2.Exception);
            Assert.True(finalPromise2.IsCompleted, "Final Task completed");
            Assert.True(await result2.Task, "Timeout-2");

            Assert.NotEqual(0, stageNum1);  // “工作项目没有被执行-1”
            Assert.Equal(14, stageNum1);  // "工作项目执行 超出订单-1
            Assert.NotEqual(0, stageNum2);  // “工作项目未执行 2”
            Assert.Equal(22, stageNum2);  // "工作项目 执行超出订单2"


        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_Current_TaskScheduler()
        {
            UnitTestSchedulingContext context = UnitTestSchedulingContext.Create(_loggerFactory);
            var activationScheduler = context.WorkItemGroup.TaskScheduler;

            var mainDone = false;
            var stageNum1 = 0;

            var result = new TaskCompletionSource<bool>();
            var wrapperGate = new TaskCompletionSource<bool>();
            var mainTurnGate = new TaskCompletionSource<bool>();

            Task wrapper = null!;
            Task finalPromise = null!;
            var wrapperCreated = new TaskCompletionSource<Task>();

            context.Scheduler.QueueAction(() =>
            {
                Log(1, "Outer ClosureWorkItem " + Task.CurrentId + " starting");
                Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #0"

                Log(2, "Starting wrapper Task");
                wrapper = Task.Factory.StartNew(() =>
                {
                    Log(3, "Inside wrapper Task Id=" + Task.CurrentId);
                    Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #1"

                    Log(4, "Wrapper Task " + Task.CurrentId + " creating AC chain");
                    var promise1 = Task.Factory.StartNew(() =>
                    {
                        Log(5, "#1 Inside AC Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #1"
                        SubProcess1(1);
                    });
                    var promise2 = promise1.ContinueWith((_) =>
                    {
                        Log(6, "#2 Inside AC Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #2"
                        SubProcess1(2);
                    });
                    finalPromise = promise2.ContinueWith((_) =>
                    {
                        Log(7, "#3 Inside final AC Task Id=" + Task.CurrentId);
                        Assert.Equal(activationScheduler, TaskScheduler.Current);  // "TaskScheduler.Current #3"
                        SubProcess1(3);
                        result.SetResult(true);
                    });
                    finalPromise.Ignore();

                    Log(8, "Wrapper Task Id=" + Task.CurrentId + " waiting for gate");
                    wrapperGate.Task.Wait();

                    Log(9, "Wrapper Task Id=" + Task.CurrentId + " finished");

                    void SubProcess1(int n)
                    {
                        var msg = string.Format("1-{0} MainDone={1} inside Task {2}", n, mainDone, Task.CurrentId);
                        output.WriteLine("1 ===> " + msg);
                        Assert.True(mainDone, msg + " -- Main turn should be finished");
                        stageNum1 = n;
                    }
                });

                wrapperCreated.SetResult(wrapper);

                Log(10, "Outer ClosureWorkItem Task Id=" + Task.CurrentId + " waiting for gate");
                mainTurnGate.Task.Wait();
                Log(11, "Outer ClosureWorkItem Task Id=" + Task.CurrentId + " continuing");

                Log(12, "Finished Outer TaskWorkItem Task Id=" + wrapper.Id);
                mainDone = true;
            });

            Log(13, "Waiting for ClosureWorkItem to spawn wrapper Task");
            wrapper = await wrapperCreated.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.NotNull(wrapper); // 未创建任务

            // 释放大门,以便行刑
            wrapperGate.SetResult(true);
            mainTurnGate.SetResult(true);

            Log(14, "Waiting for wrapper Task Id=" + wrapper.Id + " to complete");
            await wrapper.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.False(wrapper.IsFaulted, "Wrapper Task faulted: " + wrapper.Exception);
            Assert.True(wrapper.IsCompleted, "Wrapper Task should be completed");

            Log(16, "Waiting for TaskWorkItem to complete");
            // 使用合理的超时等待 mainDone
            for (var i = 0; i < 100 && !mainDone; i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Log(17, "Done waiting for TaskWorkItem to complete MainDone=" + mainDone);
            Assert.True(mainDone, "Main Task should be completed");
            Assert.NotNull(finalPromise); // 未创建 AC 链条

            Log(18, "Waiting for final AC promise to complete");
            await finalPromise.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Log(19, "Done waiting for final promise");
            Assert.False(finalPromise.IsFaulted, "Final AC faulted: " + finalPromise.Exception);
            Assert.True(finalPromise.IsCompleted, "Final AC completed");
            Assert.True(await result.Task, "Timeout-1");

            Assert.NotEqual(0, stageNum1);  // “工作项目没有被执行-1”
            Assert.Equal(3, stageNum1);  // "工作项目执行 超出订单-1
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_ContinueWith_1_Test()
        {
            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            var result = new TaskCompletionSource<bool>();
            var gate = new SemaphoreSlim(0, 1);
            var n = 0;
            // ReSharper disable AccessToModifiedClosure
            context.Scheduler.QueueAction(() =>
            {
                var task1 = Task.Factory.StartNew(() => { output.WriteLine("===> 1a"); gate.Wait(); n = n + 3; output.WriteLine("===> 1b"); });
                var task2 = task1.ContinueWith((_) => { n = n * 5; output.WriteLine("===> 2"); });
                var task3 = task2.ContinueWith((_) => { n = n / 5; output.WriteLine("===> 3"); });
                var task4 = task3.ContinueWith((_) => { n = n - 2; output.WriteLine("===> 4"); result.SetResult(true); });
                task4.Ignore();

                // 释放大门,让任务1 完成
                gate.Release();
            });
            // ReSharper restore AccessToModifiedClosure

            await result.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(1, n);  // “工作项目执行不合秩序”
        }

        [Fact, TestCategory("Functional"), TestCategory("AsynchronyPrimitives")]
        public async Task Sched_Task_JoinAll()
        {
            var result = new TaskCompletionSource<bool>();
            var n = 0;
            Task<int>[] tasks = null!;
            var gates = new SemaphoreSlim[4];
            for (var i = 0; i < 4; i++)
            {
                gates[i] = new SemaphoreSlim(0, 1);
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            context.Scheduler.QueueAction(() =>
            {
                var task1 = Task<int>.Factory.StartNew(() => { output.WriteLine("===> 1a"); gates[0].Wait(); n = n + 3; output.WriteLine("===> 1b"); return 1; });
                var task2 = Task<int>.Factory.StartNew(() => { output.WriteLine("===> 2a"); gates[1].Wait(); n = n + 3; output.WriteLine("===> 2b"); return 2; });
                var task3 = Task<int>.Factory.StartNew(() => { output.WriteLine("===> 3a"); gates[2].Wait(); n = n + 3; output.WriteLine("===> 3b"); return 3; });
                var task4 = Task<int>.Factory.StartNew(() => { output.WriteLine("===> 4a"); gates[3].Wait(); n = n + 3; output.WriteLine("===> 4b"); return 4; });
                tasks = new Task<int>[] { task1, task2, task3, task4 };
                result.SetResult(true);

                // 全部门按顺序释放
                for (var i = 0; i < 4; i++)
                {
                    gates[i].Release();
                }
            });

            await result.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken); // 等待主项( 创建任务的项目) 工作项目完成 。

            var promise = Task<int[]>.Factory.ContinueWhenAll(tasks, (res) =>
            {
                List<int> output = new List<int>();
                var taskNum = 1;
                foreach (var t in tasks)
                {
                    Assert.True(t.IsCompleted, "Sub-Task completed");
                    Assert.False(t.IsFaulted, "Sub-Task faulted: " + t.Exception);
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
                    var val = t.Result;
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
                    Assert.Equal(taskNum, val);  // " 任务返回的价值 " + taskNum
                    output.Add(val);
                    taskNum++;
                }
                var results = output.ToArray();
                return results;
            });

            await promise.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);

            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(12, n);  // "并非所有工作项目都执行"
            var ms = stopwatch.ElapsedMilliseconds;
            // 因为我们摘除睡眠后,执行应该更快得多——只需核实完成
            Assert.True(ms < 8000, "Wait time too long: " + ms);
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_AC_ContinueWith_2_OrleansSched()
        {
            var context = UnitTestSchedulingContext.Create(_loggerFactory);
            var workItemGroup = context.WorkItemGroup;

            var result1 = new TaskCompletionSource<bool>();
            var result2 = new TaskCompletionSource<bool>();
            var failed1 = false;
            var failed2 = false;
            var gate = new SemaphoreSlim(0, 1);

            var task1 = Task.Factory.StartNew(
                () => { output.WriteLine("===> 1a"); gate.Wait(); throw new ArgumentException(); },
                CancellationToken.None,
                TaskCreationOptions.RunContinuationsAsynchronously,
                workItemGroup.TaskScheduler);

            var task2 = task1.ContinueWith((Task t) =>
            {
                if (!t.IsFaulted) output.WriteLine("===> 2");
                else
                {
                    output.WriteLine("===> 3");
                    failed1 = true;
                    result1.SetResult(true);
                }
            },
            workItemGroup.TaskScheduler);
            var task3 = task1.ContinueWith((Task t) =>
            {
                if (!t.IsFaulted) output.WriteLine("===> 4");
                else
                {
                    output.WriteLine("===> 5");
                    failed2 = true;
                    result2.SetResult(true);
                }
            },
            workItemGroup.TaskScheduler);

            task1.Ignore();
            task2.Ignore();
            task3.Ignore();

            // 释放大门,允许任务1 丢弃
            gate.Release();

            await result1.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            await result2.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.True(failed1);  // "第一 ContinueWith 没有发射错误处理器"
            Assert.True(failed2);  // 第二 ContinueWith 没有发射错误处理器
        }

        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_Task_SchedulingContext()
        {
            var context = UnitTestSchedulingContext.Create(_loggerFactory);

            var result = new TaskCompletionSource<bool>();
            Task endOfChain = null!;
            var n = 0;
            var gate = new SemaphoreSlim(0, 1);

            Task wrapper = new Task(() =>
            {
                CheckRuntimeContext(context);

                // ReSharper disable AccessToModifiedClosure
                var task1 = Task.Factory.StartNew(() =>
                {
                    output.WriteLine("===> 1a ");
                    CheckRuntimeContext(context);
                    gate.Wait();
                    n = n + 3;
                    output.WriteLine("===> 1b");
                    CheckRuntimeContext(context);
                });
                var task2 = task1.ContinueWith(task =>
                {
                    output.WriteLine("===> 2");
                    CheckRuntimeContext(context);
                    n = n * 5;
                });
                var task3 = task2.ContinueWith(task =>
                {
                    output.WriteLine("===> 3");
                    n = n / 5;
                    CheckRuntimeContext(context);
                });
                var task4 = task3.ContinueWith(task =>
                {
                    output.WriteLine("===> 4");
                    n = n - 2;
                    result.SetResult(true);
                    CheckRuntimeContext(context);
                });
                // ReSharper restore AccessToModifiedClosure
                endOfChain = task4.ContinueWith(task =>
                {
                    output.WriteLine("Done Faulted={0}", task.IsFaulted);
                    CheckRuntimeContext(context);
                    Assert.False(task.IsFaulted, "Faulted with Exception=" + task.Exception);
                });

                // 释放大门,让任务1 完成
                gate.Release();
            });
            wrapper.Start(context.WorkItemGroup.TaskScheduler);
            await wrapper.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);

            Assert.False(wrapper.IsFaulted, "Wrapper Task Faulted with Exception=" + wrapper.Exception);
            Assert.True(wrapper.IsCompleted, "Wrapper Task completed");
            await result.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
            Assert.NotNull(endOfChain); // 成功创建了链条尾端任务
            Assert.False(endOfChain.IsFaulted, "Task chain Faulted with Exception=" + endOfChain.Exception);
            Assert.True(n != 0, "Work items did not get executed");
            Assert.Equal(1, n);  // “工作项目执行不合秩序”
        }

        private void Log(int level, string what)
        {
            output.WriteLine("#{0} - {1} -- Thread={2} Worker={3} TaskScheduler.Current={4}",
                level, what,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name!,
                TaskScheduler.Current!);
        }

        private static void CheckRuntimeContext(IGrainContext context)
        {
            Assert.NotNull(RuntimeContext.Current); // 运行时间环境不应无效
            Assert.NotNull(RuntimeContext.Current); // 启动环境不应无效
            Assert.Equal(context, RuntimeContext.Current);  // “启动环境”
        }
    }
}

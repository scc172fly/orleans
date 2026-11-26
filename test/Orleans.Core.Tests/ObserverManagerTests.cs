using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Utilities;
using Xunit;

namespace NonSilo.Tests;

[TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class ObserverManagerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Notify_NullCallback_ThrowsWithoutChangingSubscriptions(bool asynchronous, bool hasObservers)
    {
        var now = DateTime.UnixEpoch;
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance)
        {
            GetDateTime = () => now,
        };
        if (hasObservers)
        {
            observerManager.Subscribe(1, 10);
            now = now.AddHours(2);
            observerManager.Subscribe(2, 20);
            observerManager.Subscribe(3, 30);
        }

        var expectedObservers = observerManager.Observers;
        var timeReads = 0;
        observerManager.GetDateTime = () =>
        {
            timeReads++;
            return now;
        };
        var predicateCalls = 0;
        bool Predicate(int observer)
        {
            predicateCalls++;
            return observer != 30;
        }

        var error = asynchronous
            ? await Record.ExceptionAsync(() => observerManager.Notify((Func<int, Task>)null!, Predicate))
            : Record.Exception(() => observerManager.Notify((Action<int>)null!, Predicate));

        Assert.Equal(expectedObservers.OrderBy(entry => entry.Key), observerManager.Observers.OrderBy(entry => entry.Key));
        Assert.Equal(0, timeReads);
        Assert.Equal(0, predicateCalls);
        var exception = Assert.IsType<ArgumentNullException>(error);
        Assert.Equal("notification", exception.ParamName);
    }

    [Fact]
    public async Task CollectionModified()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 使用前置数据来比较容易地模拟插入项,同时进行数字计算。
        bool Predicate(int observer)
        {
            if (observer == 5)
            {
                observerManager.Subscribe(11, 1);
            }

            return true;
        }

        Task NotificationAsync(int observer) => Task.CompletedTask;

        var ex = await Record.ExceptionAsync(() => observerManager.Notify(NotificationAsync, Predicate));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SubscribeDuringNotify_ObserversAreUpdatedImmediately()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();
        var newlyAddedObserverNotified = false;

        // 聘请一些初步观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);

            // 在通知第一观察员期间增加一名新观察员。
            if (observer == 0)
            {
                observerManager.Subscribe(100, 100);

                // 校验新增加的观察员是否立即在收藏中可见。
                Assert.Contains(100, observerManager.Observers.Values);
            }

            // 检查我们是否正在处理新加入的观察家
            if (observer == 100)
            {
                newlyAddedObserverNotified = true;
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 只有原始观察员才应得到通知,而不是通知期间增加的观察员。
        Assert.Equal(5, notifiedObservers.Count);

        // 新的观察员没有得到通知。
        Assert.DoesNotContain(100, notifiedObservers);
        Assert.False(newlyAddedObserverNotified);
    }

    [Fact]
    public async Task UnsubscribeDuringNotify_ObserversAreUpdatedImmediately()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();
        var unsubscribedObserverNotified = false;

        // 聘请一些初步观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);

            // 在第一观察员通知期间不订阅观察员。
            if (observer == 0)
            {
                observerManager.Unsubscribe(3);

                // 核实未订阅的观察员是否立即从收藏中移除。
                Assert.DoesNotContain(3, observerManager.Observers.Values);
            }

            // 检查一下我们是否正在处理未注册的观察者
            if (observer == 3)
            {
                unsubscribedObserverNotified = true;
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 尽管我们在迭代期间没有被订名,但照片上仍然有原始观察员。
        // 所有原始观察员均因快照而获得通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 观察员3之所以得到通知,是因为其是在快照中。
        Assert.True(unsubscribedObserverNotified);
        // 但是在通知完成后 已经不在收集了
        Assert.Equal(4, observerManager.Count);
    }

    [Fact]
    public void SyncNotify_SubscribeAndUnsubscribe_WorksCorrectly()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 聘请一些初步观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        void Notification(int observer)
        {
            notifiedObservers.Add(observer);

            // 在通知中增加新的观察员并删除现有的观察员。
            if (observer == 0)
            {
                observerManager.Subscribe(100, 100);
                observerManager.Unsubscribe(3);
            }
        }

        observerManager.Notify(Notification);

        // 所有原始观察员都得到通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 观察员3之所以得到通知,是因为其是在快照中。
        Assert.Contains(3, notifiedObservers);
        // 新观察家没有在原始的快照中
        Assert.DoesNotContain(100, notifiedObservers);
        // 5 原为1,删除+ 1 加= 5 总计。
        Assert.Equal(5, observerManager.Count);
    }

    [Fact]
    public async Task ExceptionInNotificationCallback_RemovesObserver()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 聘请一些观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            // 将特定观察员的例外排除在外。
            if (observer == 2)
            {
                throw new InvalidOperationException("Test exception");
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 应撤除一名观察员。
        Assert.Equal(4, observerManager.Count);
        // 由于例外情况,观察员2应被撤除。
        Assert.DoesNotContain(2, observerManager.Observers.Values);
    }

    [Fact]
    public void SyncNotify_ExceptionInNotificationCallback_RemovesObserver()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 聘请一些观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        void Notification(int observer)
        {
            // 将特定观察员的例外排除在外。
            if (observer == 2)
            {
                throw new InvalidOperationException("Test exception");
            }
        }

        observerManager.Notify(Notification);

        // 应撤除一名观察员。
        Assert.Equal(4, observerManager.Count);
        // 由于例外情况,观察员2应被撤除。
        Assert.DoesNotContain(2, observerManager.Observers.Values);
    }

    [Fact]
    public async Task ExpiredObservers_AreRemovedDuringNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromMinutes(30), NullLogger.Instance);
        var currentTime = DateTime.UtcNow;
        var notifiedObservers = new List<int>();

        // 模拟 GetDateTime 控制时间 。
        observerManager.GetDateTime = () => currentTime;

        // 聘请一些观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 将一些观察者移入过去,使其过期。
        // 提前一小时到
        currentTime = DateTime.UtcNow.AddHours(1);

        // 通知函数,可追踪被召唤的观察员所在的轨道。
        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);
            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 观察员的所有发言均过期,不应通知任何观察员。
        Assert.Empty(notifiedObservers);
        // 所有观察员因任期届满应被撤职。
        Assert.Equal(0, observerManager.Count);
    }

    [Fact]
    public void ClearExpired_RemovesOnlyExpiredObservers()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromMinutes(30), NullLogger.Instance);
        var startTime = DateTime.UtcNow;

        // 模拟 GetDateTime 控制时间 。
        observerManager.GetDateTime = () => startTime;

        // 聘请一些观察员。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 半数观察员最后一次看到最新情况。
        var halfTime = startTime.AddMinutes(25);
        observerManager.GetDateTime = () => halfTime;

        for (var i = 5; i < 10; i++)
        {
            // 这将刷新 LastSeen 时间 。
            observerManager.Subscribe(i, i);
        }

        // 仅前半期到期的预发时间。
        observerManager.GetDateTime = () => startTime.AddMinutes(35);

        observerManager.ClearExpired();

        // 一半因过期应删除。
        Assert.Equal(5, observerManager.Count);
        for (var i = 0; i < 5; i++)
        {
            // 上半部分应删除。
            Assert.DoesNotContain(i, observerManager.Observers.Values);
        }

        for (var i = 5; i < 10; i++)
        {
            // 下半部分应保留。
            Assert.Contains(i, observerManager.Observers.Values);
        }
    }

    [Fact]
    public void Clear_RemovesAllObservers()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 聘请一些观察员。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 校验初始数 。
        Assert.Equal(10, observerManager.Count);

        observerManager.Clear();

        Assert.Equal(0, observerManager.Count);
        Assert.Empty(observerManager.Observers);
    }

    [Fact]
    public async Task CanModifyObserversConcurrently()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1);
        const int observerCount = 100;

        // 任务1:登记观察员。
        var task1 = Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < observerCount; i++)
            {
                observerManager.Subscribe(i, i);
            }
        },
        CancellationToken.None,
        TaskCreationOptions.RunContinuationsAsynchronously,
        scheduler.ConcurrentScheduler);

        // 任务2:还认可同样的观察员(测试更新设想方案)。
        var task2 = Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < observerCount; i++)
            {
                // 测试更新的不同值 。
                observerManager.Subscribe(i, i * 10);
            }
        },
        CancellationToken.None,
        TaskCreationOptions.RunContinuationsAsynchronously,
        scheduler.ConcurrentScheduler);

        // 等待完成这两项任务。
        await Task.WhenAll(task1, task2);

        Assert.Equal(observerCount, observerManager.Count);

        // 现在对主线修改时, 以不同的线索运行通知 。
        var notifiedCount = 0;
        var tcs = new TaskCompletionSource();
        var startedTcs = new TaskCompletionSource();
        var notifyTask = Task.Factory.StartNew(async () =>
        {
            await observerManager.Notify(async observer =>
            {
                startedTcs.TrySetResult();
                await tcs.Task;
                Interlocked.Increment(ref notifiedCount);
            });
        },
        CancellationToken.None,
        TaskCreationOptions.RunContinuationsAsynchronously,
        scheduler.ConcurrentScheduler).Unwrap();
        await startedTcs.Task;

        // 同时修改收藏 。
        for (var i = observerCount; i < observerCount + 50; i++)
        {
            observerManager.Subscribe(i, i);
        }

        tcs.TrySetResult();
        await notifyTask;

        // 我们本应该通知 observerCount 观察员的
        // (在快照诞生时在场的)
        Assert.Equal(observerCount, notifiedCount);
        Assert.Equal(observerCount + 50, observerManager.Count);
    }

    [Fact]
    public void PredicateFiltersObserversToNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 聘请一些观察员。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        bool EvenNumberPredicate(int observer) => observer % 2 == 0;

        void Notification(int observer)
        {
            notifiedObservers.Add(observer);
        }

        observerManager.Notify(Notification, EvenNumberPredicate);

        // 即使是观察员也应得到通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 所有被通知的观察员应均衡。
        Assert.All(notifiedObservers, observer => Assert.True(observer % 2 == 0));
    }

    [Fact]
    public async Task AsyncPredicateFiltersObserversToNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 聘请一些观察员。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        bool OddNumberPredicate(int observer) => observer % 2 == 1;

        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);
            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync, OddNumberPredicate);

        // 只有奇特的观察员才应获得通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 所有已通知的观察员应奇特。
        Assert.All(notifiedObservers, observer => Assert.True(observer % 2 == 1));
    }

    [Fact]
    public void EnumeratorWorksCorrectly()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 聘请一些观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        var enumeratedObservers = new List<int>();
        foreach (var observer in observerManager)
        {
            enumeratedObservers.Add(observer);
        }

        Assert.Equal(5, enumeratedObservers.Count);
        Assert.Equal(Enumerable.Range(0, 5), enumeratedObservers.OrderBy(x => x));
    }

    [Fact]
    public void SubscribingExistingObserver_UpdatesLastSeenTime()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromMinutes(30), NullLogger.Instance);
        var startTime = DateTime.UtcNow;

        // 模拟 GetDateTime 控制时间 。
        observerManager.GetDateTime = () => startTime;

        // 订阅初始观察员。
        observerManager.Subscribe(1, 42);

        // 接近到期的提前时间,但还不是完全。
        var laterTime = startTime.AddMinutes(25);
        observerManager.GetDateTime = () => laterTime;

        // 更新订阅( 同号ID, 观察员值) 。
        observerManager.Subscribe(1, 42);

        // 超过原订阅时间+到期的提前时间。
        var expirationCheckTime = startTime.AddMinutes(35);
        observerManager.GetDateTime = () => expirationCheckTime;

        var notified = false;
        observerManager.Notify(observer => notified = true);

        // 由于情况好转,观察员仍应积极活动。
        Assert.True(notified);
        Assert.Equal(1, observerManager.Count);
    }

    [Fact]
    public void SubscribingExistingObserver_UpdatesObserverValue()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅初始观察员。
        observerManager.Subscribe(1, 100);

        // 更新订阅( 同ID, 不同的观察者值) 。
        observerManager.Subscribe(1, 200);

        var observedValue = 0;
        observerManager.Notify(observer => observedValue = observer);

        // 应该得到更新值。
        Assert.Equal(200, observedValue);
    }

    [Fact]
    public void SetExpirationDuration_AffectsExpiration()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var startTime = DateTime.UtcNow;

        // 模拟 GetDateTime 控制时间 。
        observerManager.GetDateTime = () => startTime;

        // 聘请一些观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 签署后期满改为较短期限。
        observerManager.ExpirationDuration = TimeSpan.FromMinutes(30);

        // 预发时间间隔为新旧的到期期限。
        // 30分钟前,但1小时之前
        var laterTime = startTime.AddMinutes(45);
        observerManager.GetDateTime = () => laterTime;

        var notifiedObservers = new List<int>();
        observerManager.Notify(observer => notifiedObservers.Add(observer));

        // 任何观察员不得因任期届满而得到通知。
        Assert.Empty(notifiedObservers);
        // 所有观察员均应被撤职。
        Assert.Equal(0, observerManager.Count);
    }

    [Fact]
    public async Task ClearDuringNotification_WorksCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new ConcurrentBag<int>();

        // 聘请一些观察员。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 启动通知。
        var notifyStartedTcs = new TaskCompletionSource();
        var notifyTask = Task.Run(() =>
        {
            observerManager.Notify(observer =>
            {
                notifyStartedTcs.TrySetResult();
                notifiedObservers.Add(observer);
            });
        }, cancellationToken);

        // 通知发生时清楚无误 。
        await notifyStartedTcs.Task.WaitAsync(cancellationToken);
        observerManager.Clear();

        // 等待通知完成。
        await notifyTask.WaitAsync(cancellationToken);

        // 声明 - 我们还是应该通知所有原始观察员
        Assert.Equal(10, notifiedObservers.Count);

        // 但收藏现在应该是空的 。
        Assert.Equal(0, observerManager.Count);
        Assert.Empty(observerManager.Observers);
    }

    [Fact]
    public void ModifyDuringEnumeration_WorksWithoutExceptions()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 聘请一些初步观察员。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        var enumeratedObservers = new List<int>();
        var newObserversAdded = new List<int>();
        var removedObserver = -1;

        // 使用在引擎盖下使用 GetEnumerator 的每个数字。
        foreach (var observer in observerManager)
        {
            enumeratedObservers.Add(observer);

            // 在点名时添加和删除观察员。
            if (observer == 2)
            {
                // 增加新的观察员。
                for (var i = 10; i < 15; i++)
                {
                    observerManager.Subscribe(i, i);
                    newObserversAdded.Add(i);
                }

                // 删除观察者。
                observerManager.Unsubscribe(4);
                removedObserver = 4;

                // 核实收集中的立即可见度(但不在查点中)。
                Assert.DoesNotContain(4, observerManager.Observers.Values);
                foreach (var newObserver in newObserversAdded)
                {
                    Assert.Contains(newObserver, observerManager.Observers.Values);
                }
            }
        }

        // 原始点数应全部与原观察员一起完成。
        Assert.Equal(5, enumeratedObservers.Count);
        // 即便我们在点数时删除了它
        Assert.Contains(removedObserver, enumeratedObservers);

        // 验证最后收集状态 。
        // 5 原为 - 1 去除+ 5 加= 9 共计。
        Assert.Equal(9, observerManager.Count);
        Assert.DoesNotContain(removedObserver, observerManager);
        foreach (var newObserver in newObserversAdded)
        {
            Assert.Contains(newObserver, observerManager);
        }
    }
}

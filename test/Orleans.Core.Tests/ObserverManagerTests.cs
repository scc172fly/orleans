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

        // 使用谓词，以便更容易模拟枚举期间插入项的情形。
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

        // 订阅一些初始观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);

            // 在通知第一个观察者期间添加一个新观察者。
            if (observer == 0)
            {
                observerManager.Subscribe(100, 100);

                // 验证新添加的观察者立即在集合中可见。
                Assert.Contains(100, observerManager.Observers.Values);
            }

            // 检查当前是否正在处理新添加的观察者。
            if (observer == 100)
            {
                newlyAddedObserverNotified = true;
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 应当只通知原有的观察者，不通知通知期间新增的观察者。
        Assert.Equal(5, notifiedObservers.Count);

        // 新添加的观察者未收到通知。
        Assert.DoesNotContain(100, notifiedObservers);
        Assert.False(newlyAddedObserverNotified);
    }

    [Fact]
    public async Task UnsubscribeDuringNotify_ObserversAreUpdatedImmediately()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();
        var unsubscribedObserverNotified = false;

        // 订阅一些初始观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);

            // 在通知第一个观察者期间取消某个观察者的订阅。
            if (observer == 0)
            {
                observerManager.Unsubscribe(3);

                // 验证取消订阅的观察者立即从集合中移除。
                Assert.DoesNotContain(3, observerManager.Observers.Values);
            }

            // 检查当前是否正在处理已取消订阅的观察者。
            if (observer == 3)
            {
                unsubscribedObserverNotified = true;
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 尽管在迭代期间取消了订阅，快照中仍包含原有的观察者。
        // 由于使用了快照，所有原有观察者均收到通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 观察者 3 因位于快照中而收到通知。
        Assert.True(unsubscribedObserverNotified);
        // 但通知完成后，它已不在集合中。
        Assert.Equal(4, observerManager.Count);
    }

    [Fact]
    public void SyncNotify_SubscribeAndUnsubscribe_WorksCorrectly()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 订阅一些初始观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        void Notification(int observer)
        {
            notifiedObservers.Add(observer);

            // 在通知期间添加新观察者并移除现有观察者。
            if (observer == 0)
            {
                observerManager.Subscribe(100, 100);
                observerManager.Unsubscribe(3);
            }
        }

        observerManager.Notify(Notification);

        // 所有原有观察者均收到通知。
        Assert.Equal(5, notifiedObservers.Count);
        // 观察者 3 因位于快照中而收到通知。
        Assert.Contains(3, notifiedObservers);
        // 新观察者不在原始快照中。
        Assert.DoesNotContain(100, notifiedObservers);
        // 原有 5 个 - 移除 1 个 + 新增 1 个 = 共 5 个。
        Assert.Equal(5, observerManager.Count);
    }

    [Fact]
    public async Task ExceptionInNotificationCallback_RemovesObserver()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅一些观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        async Task NotificationAsync(int observer)
        {
            // 针对指定观察者引发异常。
            if (observer == 2)
            {
                throw new InvalidOperationException("Test exception");
            }

            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 应移除一个观察者。
        Assert.Equal(4, observerManager.Count);
        // 应因异常移除观察者 2。
        Assert.DoesNotContain(2, observerManager.Observers.Values);
    }

    [Fact]
    public void SyncNotify_ExceptionInNotificationCallback_RemovesObserver()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅一些观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        void Notification(int observer)
        {
            // 针对指定观察者引发异常。
            if (observer == 2)
            {
                throw new InvalidOperationException("Test exception");
            }
        }

        observerManager.Notify(Notification);

        // 应移除一个观察者。
        Assert.Equal(4, observerManager.Count);
        // 应因异常移除观察者 2。
        Assert.DoesNotContain(2, observerManager.Observers.Values);
    }

    [Fact]
    public async Task ExpiredObservers_AreRemovedDuringNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromMinutes(30), NullLogger.Instance);
        var currentTime = DateTime.UtcNow;
        var notifiedObservers = new List<int>();

        // 模拟 GetDateTime 以控制时间。
        observerManager.GetDateTime = () => currentTime;

        // 订阅一些观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 将部分观察者的时间移至过去，使其过期。
        // 将时间推进 1 小时。
        currentTime = DateTime.UtcNow.AddHours(1);

        // 记录哪些观察者被调用的通知函数。
        async Task NotificationAsync(int observer)
        {
            notifiedObservers.Add(observer);
            await Task.CompletedTask;
        }

        await observerManager.Notify(NotificationAsync);

        // 由于所有观察者均已过期，不应通知任何观察者。
        Assert.Empty(notifiedObservers);
        // 应因过期移除所有观察者。
        Assert.Equal(0, observerManager.Count);
    }

    [Fact]
    public void ClearExpired_RemovesOnlyExpiredObservers()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromMinutes(30), NullLogger.Instance);
        var startTime = DateTime.UtcNow;

        // 模拟 GetDateTime 以控制时间。
        observerManager.GetDateTime = () => startTime;

        // 订阅一些观察者。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 更新一半观察者的最近可见时间。
        var halfTime = startTime.AddMinutes(25);
        observerManager.GetDateTime = () => halfTime;

        for (var i = 5; i < 10; i++)
        {
            // 这会刷新 LastSeen 时间。
            observerManager.Subscribe(i, i);
        }

        // 推进时间，使仅前一半观察者过期。
        observerManager.GetDateTime = () => startTime.AddMinutes(35);

        observerManager.ClearExpired();

        // 应因过期移除一半观察者。
        Assert.Equal(5, observerManager.Count);
        for (var i = 0; i < 5; i++)
        {
            // 前一半应被移除。
            Assert.DoesNotContain(i, observerManager.Observers.Values);
        }

        for (var i = 5; i < 10; i++)
        {
            // 后一半应保留。
            Assert.Contains(i, observerManager.Observers.Values);
        }
    }

    [Fact]
    public void Clear_RemovesAllObservers()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅一些观察者。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 验证初始数量。
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

        // 任务 1：订阅观察者。
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

        // 任务 2：也订阅相同的观察者（用于测试更新场景）。
        var task2 = Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < observerCount; i++)
            {
                // 使用不同的值测试更新。
                observerManager.Subscribe(i, i * 10);
            }
        },
        CancellationToken.None,
        TaskCreationOptions.RunContinuationsAsynchronously,
        scheduler.ConcurrentScheduler);

        // 等待两个任务完成。
        await Task.WhenAll(task1, task2);

        Assert.Equal(observerCount, observerManager.Count);

        // 现在在另一线程上执行通知，同时在主线程上修改集合。
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

        // 同时修改集合。
        for (var i = observerCount; i < observerCount + 50; i++)
        {
            observerManager.Subscribe(i, i);
        }

        tcs.TrySetResult();
        await notifyTask;

        // 应该恰好通知 observerCount 个观察者
        // （即创建快照时存在的观察者）。
        Assert.Equal(observerCount, notifiedCount);
        Assert.Equal(observerCount + 50, observerManager.Count);
    }

    [Fact]
    public void PredicateFiltersObserversToNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 订阅一些观察者。
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

        // 应仅通知编号为偶数的观察者。
        Assert.Equal(5, notifiedObservers.Count);
        // 收到通知的观察者编号均应为偶数。
        Assert.All(notifiedObservers, observer => Assert.True(observer % 2 == 0));
    }

    [Fact]
    public async Task AsyncPredicateFiltersObserversToNotify()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new List<int>();

        // 订阅一些观察者。
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

        // 应仅通知编号为奇数的观察者。
        Assert.Equal(5, notifiedObservers.Count);
        // 收到通知的观察者编号均应为奇数。
        Assert.All(notifiedObservers, observer => Assert.True(observer % 2 == 1));
    }

    [Fact]
    public void EnumeratorWorksCorrectly()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅一些观察者。
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

        // 模拟 GetDateTime 以控制时间。
        observerManager.GetDateTime = () => startTime;

        // 订阅初始观察者。
        observerManager.Subscribe(1, 42);

        // 推进时间至接近但尚未达到过期时间。
        var laterTime = startTime.AddMinutes(25);
        observerManager.GetDateTime = () => laterTime;

        // 更新订阅（相同 ID、相同观察者值）。
        observerManager.Subscribe(1, 42);

        // 推进时间至超过原始订阅时间加上过期时长的时刻。
        var expirationCheckTime = startTime.AddMinutes(35);
        observerManager.GetDateTime = () => expirationCheckTime;

        var notified = false;
        observerManager.Notify(observer => notified = true);

        // 由于进行了刷新，观察者应仍处于有效状态。
        Assert.True(notified);
        Assert.Equal(1, observerManager.Count);
    }

    [Fact]
    public void SubscribingExistingObserver_UpdatesObserverValue()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅初始观察者。
        observerManager.Subscribe(1, 100);

        // 更新订阅（相同 ID、不同观察者值）。
        observerManager.Subscribe(1, 200);

        var observedValue = 0;
        observerManager.Notify(observer => observedValue = observer);

        // 应获取更新后的值。
        Assert.Equal(200, observedValue);
    }

    [Fact]
    public void SetExpirationDuration_AffectsExpiration()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var startTime = DateTime.UtcNow;

        // 模拟 GetDateTime 以控制时间。
        observerManager.GetDateTime = () => startTime;

        // 订阅一些观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 订阅后，将过期时长改为更短的时长。
        observerManager.ExpirationDuration = TimeSpan.FromMinutes(30);

        // 推进时间至新旧过期时长之间。
        // 超过 30 分钟，但不到 1 小时。
        var laterTime = startTime.AddMinutes(45);
        observerManager.GetDateTime = () => laterTime;

        var notifiedObservers = new List<int>();
        observerManager.Notify(observer => notifiedObservers.Add(observer));

        // 由于过期，不应通知任何观察者。
        Assert.Empty(notifiedObservers);
        // 应移除所有观察者。
        Assert.Equal(0, observerManager.Count);
    }

    [Fact]
    public async Task ClearDuringNotification_WorksCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);
        var notifiedObservers = new ConcurrentBag<int>();

        // 订阅一些观察者。
        for (var i = 0; i < 10; i++)
        {
            observerManager.Subscribe(i, i);
        }

        // 开始通知。
        var notifyStartedTcs = new TaskCompletionSource();
        var notifyTask = Task.Run(() =>
        {
            observerManager.Notify(observer =>
            {
                notifyStartedTcs.TrySetResult();
                notifiedObservers.Add(observer);
            });
        }, cancellationToken);

        // 在通知进行期间清空集合。
        await notifyStartedTcs.Task.WaitAsync(cancellationToken);
        observerManager.Clear();

        // 等待通知完成。
        await notifyTask.WaitAsync(cancellationToken);

        // 断言：仍应通知快照中的所有原有观察者。
        Assert.Equal(10, notifiedObservers.Count);

        // 但此时集合应为空。
        Assert.Equal(0, observerManager.Count);
        Assert.Empty(observerManager.Observers);
    }

    [Fact]
    public void ModifyDuringEnumeration_WorksWithoutExceptions()
    {
        var observerManager = new ObserverManager<int, int>(TimeSpan.FromHours(1), NullLogger.Instance);

        // 订阅一些初始观察者。
        for (var i = 0; i < 5; i++)
        {
            observerManager.Subscribe(i, i);
        }

        var enumeratedObservers = new List<int>();
        var newObserversAdded = new List<int>();
        var removedObserver = -1;

        // 使用 foreach 进行枚举，其底层使用 GetEnumerator。
        foreach (var observer in observerManager)
        {
            enumeratedObservers.Add(observer);

            // 在枚举期间添加和移除观察者。
            if (observer == 2)
            {
                // 添加新观察者。
                for (var i = 10; i < 15; i++)
                {
                    observerManager.Subscribe(i, i);
                    newObserversAdded.Add(i);
                }

                // 移除一个观察者。
                observerManager.Unsubscribe(4);
                removedObserver = 4;

                // 验证集合中的变化立即可见（但枚举中不可见）。
                Assert.DoesNotContain(4, observerManager.Observers.Values);
                foreach (var newObserver in newObserversAdded)
                {
                    Assert.Contains(newObserver, observerManager.Observers.Values);
                }
            }
        }

        // 原有枚举应遍历所有原有观察者并完成。
        Assert.Equal(5, enumeratedObservers.Count);
        // 即使我们在枚举期间将其移除了。
        Assert.Contains(removedObserver, enumeratedObservers);

        // 验证集合的最终状态。
        // 原有 5 个 - 移除 1 个 + 新增 5 个 = 共 9 个。
        Assert.Equal(9, observerManager.Count);
        Assert.DoesNotContain(removedObserver, observerManager);
        foreach (var newObserver in newObserversAdded)
        {
            Assert.Contains(newObserver, observerManager);
        }
    }
}

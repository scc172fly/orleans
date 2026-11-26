using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using Orleans.Caching;
using Orleans.Caching.Internal;
using Xunit;

namespace NonSilo.Tests.Caching;

/// <summary>
/// 压力测试 ConcurrentLruCache 用于在高同时载荷下核查线条安全和一致性。
/// 发现种族状况、僵局和数据腐败
/// 暗藏处必须保持多代结构的一致性。
/// 即使在极端同时进入模式下也是如此。
/// </summary>
[TestCategory("BVT")]
[TestSuite("BVT")]
[TestSuite("Nightly")]
[TestProvider("None")]
[TestArea("Runtime")]
public sealed class ConcurrentLruCacheSoakTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private const int HotCap = 3;
    private const int WarmCap = 3;
    private const int ColdCap = 3;

    private const int Capacity = HotCap + WarmCap + ColdCap;

    private ConcurrentLruCache<int, string> lru = new ConcurrentLruCache<int, string>(Capacity, EqualityComparer<int>.Default);

    public ConcurrentLruCacheSoakTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    /// <summary>
    /// 测试同时进行的 GetOrAdd 操作,以确保缓存在重读/写负载下保持一致性。
    /// 证明缓存规模仍在限制之内,内部结构仍然有效。
    /// </summary>
    [Fact]
    public async Task WhenSoakConcurrentGetCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    lru.GetOrAdd(i + 1, i => i.ToString());
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            // 允许+/- 1 能力差异
            lru.Count.Should().BeInRange(7, 10);
            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetWithArgCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    // 使用参数超载
                    lru.GetOrAdd(i + 1, (i, s) => i.ToString(), "Foo");
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            // 允许+/- 1 能力差异
            lru.Count.Should().BeInRange(7, 10);
            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetAndRemoveCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    lru.TryRemove(i + 1);
                    lru.GetOrAdd(i + 1, i => i.ToString());
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetAndRemoveKvpCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    lru.TryRemove(new KeyValuePair<int, string>(i + 1, (i + 1).ToString()));
                    lru.GetOrAdd(i + 1, i => i.ToString());
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetAndUpdateCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    lru.TryUpdate(i + 1, i.ToString());
                    lru.GetOrAdd(i + 1, i => i.ToString());
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetAndAddCacheEndsInConsistentState()
    {
        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                for (var i = 0; i < 100000; i++)
                {
                    lru.AddOrUpdate(i + 1, i.ToString());
                    lru.GetOrAdd(i + 1, i => i.ToString());
                }
            });

            testOutputHelper.WriteLine($"{lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

            RunIntegrityCheck();
        }
    }

    [Fact]
    public async Task WhenSoakConcurrentGetAndUpdateValueTypeCacheEndsInConsistentState()
    {
        var lruVT = new ConcurrentLruCache<int, Guid>(Capacity, EqualityComparer<int>.Default);

        for (var i = 0; i < 10; i++)
        {
            await Threaded.Run(4, () =>
            {
                var b = new byte[8];
                for (var i = 0; i < 100000; i++)
                {
                    lruVT.TryUpdate(i + 1, new Guid(i, 0, 0, b));
                    lruVT.GetOrAdd(i + 1, x => new Guid(x, 0, 0, b));
                }
            });

            testOutputHelper.WriteLine($"{lruVT.HotCount} {lruVT.WarmCount} {lruVT.ColdCount}");
            testOutputHelper.WriteLine(string.Join(" ", lruVT.Keys));

            ConcurrentLruCacheIntegrityChecker.Validate(lruVT);
        }
    }

    [Fact]
    public async Task WhenAddingCacheSizeItemsNothingIsEvicted()
    {
        const int size = 1024;

        var cache = new ConcurrentLruCache<int, int>(size);

        await Threaded.Run(4, () =>
        {
            for (var i = 0; i < size; i++)
            {
                cache.GetOrAdd(i, k => k);
            }
        });

        cache.Metrics.Evicted.Should().Be(0);
    }

    [Fact]
    public async Task WhenConcurrentUpdateAndRemoveKvp()
    {
        var tcs = new TaskCompletionSource<int>();

        var removal = Task.Run(() =>
        {
            while (!tcs.Task.IsCompleted)
            {
                lru.TryRemove(new KeyValuePair<int, string>(5, "x"));
            }
        }, TestContext.Current.CancellationToken);

        for (var i = 0; i < 100_000; i++)
        {
            lru.AddOrUpdate(5, "a");
            lru.TryGet(5, out _).Should().BeTrue("key 'a' should not be deleted");
            lru.AddOrUpdate(5, "x");
        }

        tcs.SetResult(int.MaxValue);

        await removal;
    }

    [Theory]
    [Repeat(10)]
    public async Task WhenConcurrentGetAndClearCacheEndsInConsistentState(int iteration)
    {
        await Threaded.Run(4, r =>
        {
            for (var i = 0; i < 100000; i++)
            {
                // 清晰 6,250 次/千 千 乘以 1_ 000_ 000 秒
                if (r == 0 && (i & 15) == 15)
                {
                    lru.Clear();
                }

                lru.GetOrAdd(i + 1, i => i.ToString());
            }
        });

        testOutputHelper.WriteLine($"{iteration} {lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
        testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

        RunIntegrityCheck();
    }

    [Theory]
    [Repeat(10)]
    public async Task WhenConcurrentGetAndClearDuringWarmupCacheEndsInConsistentState(int iteration)
    {
        await Threaded.Run(4, r =>
        {
            for (var i = 0; i < 100000; i++)
            {
                // 清晰为每千次1_000 公里25 000次
                // 能力是9,所以在热热之前我们先清理
                if (r == 0 && (i & 3) == 3)
                {
                    lru.Clear();
                }

                lru.GetOrAdd(i + 1, i => i.ToString());
            }
        });

        testOutputHelper.WriteLine($"{iteration} {lru.HotCount} {lru.WarmCount} {lru.ColdCount}");
        testOutputHelper.WriteLine(string.Join(" ", lru.Keys));

        RunIntegrityCheck();
    }

    // 如果有活锁的话,这个测试将永远进行下去。
    // 暗藏簿记有些管理费,因此更难引起
    // 与 LruItemSoakTests.DetectTornStruct相对,在读者线内旋转。
    [Theory]
    [Repeat(10)]
    public async Task WhenValueIsBigStructNoLiveLock(int _)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource<bool>();
        var cache = new ConcurrentLruCache<int, Guid>(Capacity, EqualityComparer<int>.Default);

        var setTask = Task.Run(
            () => Setter(cache, source.Token, started),
            source.Token);
        await started.Task.WaitAsync(source.Token);
        Checker(cache, source);

        await setTask;
    }

    private void Setter(ConcurrentLruCache<int, Guid> cache, CancellationToken cancelToken, TaskCompletionSource<bool> started)
    {
        started.SetResult(true);

        while (true)
        {
            cache.AddOrUpdate(1, Guid.NewGuid());
            cache.AddOrUpdate(1, Guid.NewGuid());

            if (cancelToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void Checker(ConcurrentLruCache<int, Guid> cache, CancellationTokenSource source)
    {
        for (var count = 0; count < 100_000; ++count)
        {
            cache.TryGet(1, out _);
        }

        source.Cancel();
    }

    private void RunIntegrityCheck() => ConcurrentLruCacheIntegrityChecker.Validate(lru);

    private static class ConcurrentLruCacheIntegrityChecker
    {
        public static void Validate<K, V>(ConcurrentLruCache<K, V> cache) where K : notnull
        {
            ConcurrentLruCache<K, V>.ITestAccessor testAccessor = cache;
            // 队列计数器必须与队列一致
            testAccessor.HotQueue.Count.Should().Be(cache.HotCount, "hot queue has a corrupted count");
            testAccessor.WarmQueue.Count.Should().Be(cache.WarmCount, "warm queue has a corrupted count");
            testAccessor.ColdQueue.Count.Should().Be(cache.ColdCount, "cold queue has a corrupted count");

            // 缓存内容必须与队列项目保持一致
            ValidateQueue(testAccessor.HotQueue, "hot");
            ValidateQueue(testAccessor.WarmQueue, "warm");
            ValidateQueue(testAccessor.ColdQueue, "cold");

            // 缓存必须处于容量范围内
            cache.Count.Should().BeLessThanOrEqualTo(cache.Capacity + 1, "capacity out of valid range");

            void ValidateQueue(ConcurrentQueue<ConcurrentLruCache<K, V>.LruItem> queue, string queueName)
            {
                foreach (var item in queue)
                {
                    if (item.WasRemoved)
                    {
                        // 队列中可能包含相同键/项的2个(或更多)实例。其中1个被删除,
                        // 并添加了后 另一被删除。
                        // 在这种情况下,只有在队列中包含一个键的条目,并标为 WasRemoved 假的
                        if (testAccessor.Dictionary.TryGetValue(item.Key, out var value))
                        {
                            testAccessor.HotQueue.Union(testAccessor.WarmQueue).Union(testAccessor.ColdQueue)
                                .Any(i => i.Key.Equals(item.Key) && !i.WasRemoved)
                                .Should().BeTrue($"{queueName} removed item {item.Key} was not removed");
                        }
                    }
                    else
                    {
                        testAccessor.Dictionary.TryGetValue(item.Key, out var value).Should().BeTrue($"{queueName} item {item.Key} was not present");
                    }
                }
            }
        }
    }

    private sealed class RepeatAttribute : Xunit.v3.DataAttribute
    {
        private readonly int _count;

        public RepeatAttribute(int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(
                    paramName: nameof(count),
                    message: "Repeat count must be greater than 0."
                    );
            }

            _count = count;
        }

        public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, Xunit.Sdk.DisposalTracker disposalTracker)
        {
            IReadOnlyCollection<ITheoryDataRow> rows = Enumerable.Range(start: 1, count: _count)
                .Select(iterationNumber => (ITheoryDataRow)new TheoryDataRow(iterationNumber))
                .ToArray();
            return new(rows);
        }

        public override bool SupportsDiscoveryEnumeration() => true;
    }

    private class Threaded
    {
        public static Task Run(int threadCount, Action action)
        {
            return Run(threadCount, i => action());
        }

        public static async Task Run(int threadCount, Action<int> action)
        {
            var tasks = new Task[threadCount];
            ManualResetEvent mre = new ManualResetEvent(false);

            for (int i = 0; i < threadCount; i++)
            {
                int run = i;
                tasks[i] = Task.Run(() =>
                {
                    mre.WaitOne();
                    action(run);
                });
            }

            mre.Set();

            await Task.WhenAll(tasks);
        }

        public static Task RunAsync(int threadCount, Func<Task> action)
        {
            return Run(threadCount, i => action());
        }

        public static async Task RunAsync(int threadCount, Func<int, Task> action)
        {
            var tasks = new Task[threadCount];
            ManualResetEvent mre = new ManualResetEvent(false);

            for (int i = 0; i < threadCount; i++)
            {
                int run = i;
                tasks[i] = Task.Run(async () =>
                {
                    mre.WaitOne();
                    await action(run);
                });
            }

            mre.Set();

            await Task.WhenAll(tasks);
        }
    }
}

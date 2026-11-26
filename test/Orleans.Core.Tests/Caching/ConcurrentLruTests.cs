using AwesomeAssertions;
using System.Collections;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using Orleans.Caching;
using Orleans.Caching.Internal;
using TestExtensions;

namespace NonSilo.Tests.Caching;

/// <summary>
/// ConcurrentLruCache测试,这是 Orleans中使用的一条线索安全的 LRU(最近使用过的最小)缓存实施。
/// 这个暗藏处设计成一个多代结构(热、暖、冷),以有效管理经常被查获的物品。
/// 整个使用 Orleans 缓存 grain 目录条目, grain 参考文献和其他经常获得的数据。
/// </summary>
[TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class ConcurrentLruTests(ITestOutputHelper testOutputHelper)
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;
    private const int Capacity = 100;
    private readonly CapacityPartition _capacityPartition = new(Capacity);
    private int HotCap => _capacityPartition.Hot;
    private int WarmCap => _capacityPartition.Warm;
    private int ColdCap => _capacityPartition.Cold;

    private readonly ConcurrentLruCache<int, string> _lru = new(Capacity);
    private readonly ValueFactory _valueFactory = new();

    private static ConcurrentLruCache<int, string>.ITestAccessor GetTestAccessor(ConcurrentLruCache<int, string> lru) => lru;

    /// <summary>
    /// 证明藏匿处至少需要3个能力,以支持其多代结构。
    /// </summary>
    [Fact]
    public void WhenCapacityIsLessThan3CtorThrows()
    {
        Action constructor = () => { var x = new ConcurrentLruCache<int, string>(2, EqualityComparer<int>.Default); };

        constructor.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenCapacityIs4HotHasCapacity1AndColdHasCapacity1()
    {
        var lru = new ConcurrentLruCache<int, int>(4, EqualityComparer<int>.Default);

        for (var i = 0; i < 5; i++)
        {
            lru.GetOrAdd(i, x => x);
        }

        lru.HotCount.Should().Be(1);
        lru.ColdCount.Should().Be(1);
        lru.Capacity.Should().Be(4);
    }

    [Fact]
    public void WhenCapacityIs10HotHasCapacity1AndWarmHasCapacity8AndColdHasCapacity1()
    {
        var lru = new ConcurrentLruCache<int, int>(10, EqualityComparer<int>.Default);

        for (var i = 0; i < lru.Capacity; i++)
        {
            lru.GetOrAdd(i, x => x);
        }

        lru.HotCount.Should().Be(1);
        lru.WarmCount.Should().Be(8);
        lru.ColdCount.Should().Be(1);
        lru.Capacity.Should().Be(10);
    }

    [Fact]
    public void ConstructAddAndRetrieveWithDefaultCtorReturnsValue()
    {
        var x = new ConcurrentLruCache<int, int>(3);

        x.GetOrAdd(1, k => k).Should().Be(1);
    }

    /// <summary>
    /// 测试当添加新条目时,缓存正确跟踪项目计数。
    /// </summary>
    [Fact]
    public void WhenItemIsAddedCountIsCorrect()
    {
        _lru.Count.Should().Be(0);
        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.Count.Should().Be(1);
    }

    [Fact]
    public void WhenItemsAddedKeysContainsTheKeys()
    {
        _lru.Count.Should().Be(0);
        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);
        _lru.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void WhenItemsAddedGenericEnumerateContainsKvps()
    {
        _lru.Count.Should().Be(0);
        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);
        _lru.Should().BeEquivalentTo(new[] { new KeyValuePair<int, string>(1, "1"), new KeyValuePair<int, string>(2, "2") });
    }

    [Fact]
    public void WhenItemsAddedEnumerateContainsKvps()
    {
        _lru.Count.Should().Be(0);
        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);

        var enumerable = (IEnumerable)_lru;
        enumerable.Should().BeEquivalentTo(new[] { new KeyValuePair<int, string>(1, "1"), new KeyValuePair<int, string>(2, "2") });
    }

    [Fact]
    public void FromColdWarmupFillsWarmQueue()
    {
        FillCache();

        _lru.Count.Should().Be(Capacity);
    }

    [Fact]
    public void WhenItemExistsTryGetReturnsValueAndTrue()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);
        var result = _lru.TryGet(1, out var value);

        result.Should().Be(true);
        value.Should().Be("1");
    }

    [Fact]
    public void WhenItemDoesNotExistTryGetReturnsNullAndFalse()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);
        var result = _lru.TryGet(2, out var value);

        result.Should().Be(false);
        value.Should().BeNull();
    }

    [Fact]
    public void WhenItemIsAddedThenRetrievedMetricHitRatioIsHalf()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);
        var result = _lru.TryGet(1, out var value);

        _lru.Metrics.HitRatio.Should().Be(0.5);
    }

    [Fact]
    public void WhenItemIsAddedThenRetrievedTotalIs2()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);
        var result = _lru.TryGet(1, out var value);

        _lru.Metrics.Total.Should().Be(2);
    }

    [Fact]
    public void WhenRefToMetricsIsCapturedResultIsCorrect()
    {
        // 中此选项可检测结构结构复制的特性。如果内部数据类
        // 没用,这次测试失败了
        var m = _lru.Metrics;

        _lru.GetOrAdd(1, _valueFactory.Create);
        var result = _lru.TryGet(1, out var value);

        m.HitRatio.Should().Be(0.5);
    }

    [Fact]
    public void WhenKeyIsRequestedItIsCreatedAndCached()
    {
        var result1 = _lru.GetOrAdd(1, _valueFactory.Create);
        var result2 = _lru.GetOrAdd(1, _valueFactory.Create);

        _valueFactory.TimesCalled.Should().Be(1);
        result1.Should().Be(result2);
    }

    [Fact]
    public void WhenKeyIsRequestedWithArgItIsCreatedAndCached()
    {
        var result1 = _lru.GetOrAdd(1, _valueFactory.Create, "x");
        var result2 = _lru.GetOrAdd(1, _valueFactory.Create, "y");

        _valueFactory.TimesCalled.Should().Be(1);
        result1.Should().Be(result2);
    }

    [Fact]
    public void WhenDifferentKeysAreRequestedValueIsCreatedForEach()
    {
        var result1 = _lru.GetOrAdd(1, _valueFactory.Create);
        var result2 = _lru.GetOrAdd(2, _valueFactory.Create);

        _valueFactory.TimesCalled.Should().Be(2);

        result1.Should().Be("1");
        result2.Should().Be("2");
    }

    [Fact]
    public void WhenValuesAreNotReadAndMoreKeysRequestedThanCapacityCountDoesNotIncrease()
    {
        FillCache();

        var result = _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.Count.Should().Be(Capacity);
        _valueFactory.TimesCalled.Should().Be(Capacity + 1);
    }

    [Fact]
    public void WhenValuesAreReadAndMoreKeysRequestedThanCapacityCountIsBounded()
    {
        for (var i = 0; i < Capacity + 1; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);

            // 当物品仍热时已经缓存的触摸项
            if (i > 0)
            {
                _lru.GetOrAdd(i - 1, _valueFactory.Create);
            }
        }

        _lru.Count.Should().Be(Capacity);
        _valueFactory.TimesCalled.Should().Be(Capacity + 1);
    }

    [Fact]
    public void WhenKeysAreContinuouslyRequestedInTheOrderTheyAreAddedCountIsBounded()
    {
        for (var i = 0; i < Capacity + 10; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);

            // 触摸所有已经隐藏在热、温暖和冷的物品
            // 这是最坏的假设情况,因为我们按他们的确切顺序 来触摸他们
            // 添加了。
            for (var j = 0; j < i; j++)
            {
                _lru.GetOrAdd(j, _valueFactory.Create);
            }

            _testOutputHelper.WriteLine($"Total: {_lru.Count} Hot: {_lru.HotCount} Warm: {_lru.WarmCount} Cold: {_lru.ColdCount}");
            _lru.Count.Should().BeLessThanOrEqualTo(Capacity + 1);
        }
    }

    [Fact]
    public void WhenKeysAreContinuouslyRequestedInTheOrderTheyAreAddedCountIsBounded2()
    {
        var lru = new ConcurrentLruCache<int, string>(128, EqualityComparer<int>.Default);

        for (var i = 0; i < 128 + 10; i++)
        {
            lru.GetOrAdd(i, _valueFactory.Create);

            // 触摸所有已经隐藏在热、温暖和冷的物品
            // 这是最坏的假设情况,因为我们按他们的确切顺序 来触摸他们
            // 添加了。
            for (var j = 0; j < i; j++)
            {
                lru.GetOrAdd(j, _valueFactory.Create);
            }

            lru.Count.Should().BeLessThanOrEqualTo(128 + 1, $"Total: {lru.Count} Hot: {lru.HotCount} Warm: {lru.WarmCount} Cold: {lru.ColdCount}");
        }
    }

    /// <summary>
    /// 测试多代驱逐政策:热一代没有获得的物品被降为冷藏。
    /// 以此测试暗藏库区分经常和不经常获得的物品的能力。
    /// </summary>
    [Fact]
    public void WhenValueIsNotTouchedAndExpiresFromHotValueIsBumpedToCold()
    {
        FillCache();

        // 插入值, 使其热
        Touch(0);

        // 插入更多的值, 降为冷
        GetOrAddRangeInclusive(1, Capacity - 1);

        IsInCache(0); // 价值应当冷淡

        GetOrAddRangeInclusive(Capacity, Capacity + 1);

        // 价值本应该被拆迁
        _lru.TryGet(0, out var value).Should().BeFalse();
    }

    private bool IsInCache(int key) => _lru.Keys.Contains(key);

    private void Touch(int key)
    {
        _lru.GetOrAdd(key, _valueFactory.Create);
    }

    private void GetOrAddRangeInclusive(int start, int end)
    {
        if (start <= end)
        {
            for (var i = start; i <= end; i++)
            {
                Touch(i);
            }
        }
        else
        {
            for (var i = start; i >= end; i--)
            {
                Touch(i);
            }
        }
    }

    private void AddOrUpdateRangeInclusive(int start, int end)
    {
        if (start <= end)
        {
            for (var i = start; i <= end; i++)
            {
                _lru.AddOrUpdate(i, _valueFactory.Create(i));
            }
        }
        else
        {
            for (var i = start; i >= end; i--)
            {
                _lru.AddOrUpdate(i, _valueFactory.Create(i));
            }
        }
    }

    [Fact]
    public void WhenValueIsTouchedAndExpiresFromHotValueIsBumpedToWarm()
    {
        FillCache();

        // 热到热
        Touch(0);
        Touch(0);

        GetOrAddRangeInclusive(1, 9);

        _lru.TryGet(0, out var value).Should().BeTrue();
    }

    [Fact]
    public void WhenValueIsTouchedAndExpiresFromColdItIsBumpedToWarm()
    {
        FillCache();

        _lru.GetOrAdd(0, _valueFactory.Create);

        IsInCache(0).Should().BeTrue();


        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);
        _lru.GetOrAdd(3, _valueFactory.Create); // 将 0 推至冷( 不热触)

        IsInCache(0).Should().BeTrue();

        _lru.GetOrAdd(0, _valueFactory.Create); // 寒冷中触碰0

        IsInCache(0).Should().BeTrue();

        GetOrAddRangeInclusive(4, 9);
        _lru.GetOrAdd(4, _valueFactory.Create); // 完全循环冷, 如果零不移动到温暖的状态, 这将驱离 0
        _lru.GetOrAdd(5, _valueFactory.Create);
        _lru.GetOrAdd(6, _valueFactory.Create);
        _lru.GetOrAdd(7, _valueFactory.Create);
        _lru.GetOrAdd(8, _valueFactory.Create);
        _lru.GetOrAdd(9, _valueFactory.Create);

        _lru.TryGet(0, out var value).Should().BeTrue();
    }

    [Fact]
    public void WhenValueIsNotTouchedAndExpiresFromColdItIsRemoved()
    {
        FillCache();

        _lru.GetOrAdd(0, _valueFactory.Create);

        AddOrUpdateRangeInclusive(1, Capacity);

        _lru.TryGet(0, out var value).Should().BeFalse();
    }

    [Fact]
    public void WhenValueIsNotTouchedAndExpiresFromWarmValueIsBumpedToCold()
    {
        FillCache();

        // 插入热插
        Touch(0);

        // 重新触碰它,然后把它提升到“温暖”
        Touch(0);

        GetOrAddRangeInclusive(1, Capacity - 1);

        IsInCache(0).Should().BeTrue();

        // 更能触摸到更多的价值 让它被逐出
        for (var i = 1; i < 1 + Capacity; i++)
        {
            Touch(i);
            Touch(i);
        }

        _lru.TryGet(0, out var value).Should().BeFalse();
    }

    [Fact]
    public void WhenValueIsTouchedAndExpiresFromWarmValueIsBumpedBackIntoWarm()
    {
        FillCache();

        _lru.GetOrAdd(0, _valueFactory.Create);
        _lru.GetOrAdd(0, _valueFactory.Create); // 热的触碰 0,它会促进温暖

        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);
        _lru.GetOrAdd(3, _valueFactory.Create); // 将 0 推向热点

        // 切下3个值, 以促变暖
        _lru.GetOrAdd(4, _valueFactory.Create); _lru.GetOrAdd(4, _valueFactory.Create);
        _lru.GetOrAdd(5, _valueFactory.Create); _lru.GetOrAdd(5, _valueFactory.Create);
        _lru.GetOrAdd(6, _valueFactory.Create); _lru.GetOrAdd(6, _valueFactory.Create);

        // 将4,5,6至热,0至冷
        _lru.GetOrAdd(7, _valueFactory.Create);
        _lru.GetOrAdd(8, _valueFactory.Create);
        _lru.GetOrAdd(9, _valueFactory.Create);

        // 触碰 0
        _lru.TryGet(0, out var value).Should().BeTrue();

        // 将7,8,9推入冷却,循环0回暖
        _lru.GetOrAdd(10, _valueFactory.Create);
        _lru.GetOrAdd(11, _valueFactory.Create);
        _lru.GetOrAdd(12, _valueFactory.Create);

        _lru.TryGet(0, out value).Should().BeTrue();
    }

    [Fact]
    public void WhenValueExpiresItIsDisposed()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);
        var disposableValueFactory = new DisposableValueFactory();

        for (var i = 0; i < 7; i++)
        {
            lruOfDisposable.GetOrAdd(i, disposableValueFactory.Create);
        }

        disposableValueFactory.Items[0].IsDisposed.Should().BeTrue();

        disposableValueFactory.Items[1].IsDisposed.Should().BeFalse();
        disposableValueFactory.Items[2].IsDisposed.Should().BeFalse();
        disposableValueFactory.Items[3].IsDisposed.Should().BeFalse();
        disposableValueFactory.Items[4].IsDisposed.Should().BeFalse();
        disposableValueFactory.Items[5].IsDisposed.Should().BeFalse();
        disposableValueFactory.Items[6].IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void WhenAddingNullValueCanBeAddedAndRemoved()
    {
        _lru.GetOrAdd(1, _ => null!).Should().BeNull();
        _lru.AddOrUpdate(1, null!);
        _lru.TryRemove(1).Should().BeTrue();
    }

    [Fact]
    public void WhenValuesAreEvictedEvictionMetricCountsEvicted()
    {
        FillCache();

        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.Metrics.Evicted.Should().Be(1);
    }

    [Fact]
    public void WhenKeyExistsTryRemoveRemovesItemAndReturnsTrue()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryRemove(1).Should().BeTrue();
        _lru.TryGet(1, out var value).Should().BeFalse();
    }

    [Fact]
    public void WhenKeyExistsTryRemoveReturnsValue()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryRemove(1, out var value).Should().BeTrue();
        value.Should().Be("1");
    }

    [Fact]
    public void WhenItemIsRemovedItIsDisposed()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);
        var disposableValueFactory = new DisposableValueFactory();

        lruOfDisposable.GetOrAdd(1, disposableValueFactory.Create);
        lruOfDisposable.TryRemove(1);

        disposableValueFactory.Items[1].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void WhenItemRemovedFromHotDuringWarmupItIsEagerlyCycledOut()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryRemove(1);
        Print();                                    // [1] 热 [1] [1]温暖 [ [冷 [ ]

        _lru.GetOrAdd(1, _valueFactory.Create);
        _lru.GetOrAdd(2, _valueFactory.Create);
        _lru.GetOrAdd(3, _valueFactory.Create);
        Print();                                    // [热,1,2,3] 温暖 [冷

        _lru.WarmCount.Should().Be(0);
        _lru.ColdCount.Should().Be(0);
    }

    [Fact]
    public void WhenItemRemovedFromHotAfterWarmupItIsEagerlyCycledOut()
    {
        for (var i = 0; i < _lru.Capacity; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);
        }

        _lru.Metrics.Evicted.Should().Be(0);

        _lru.GetOrAdd(-1, _valueFactory.Create);

        _lru.TryRemove(-1);

        // 整个循环热,即3项
        foreach (var item in Enumerable.Range(1000, HotCap))
        {
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        // 热门队列满了,第四件就会被排出
        _lru.Metrics.Evicted.Should().Be(HotCap);
    }

    [Fact]
    public void WhenItemRemovedFromWarmDuringWarmupItIsEagerlyCycledOut()
    {
        foreach (var item in Enumerable.Range(1, HotCap))
        {
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        _lru.TryRemove(1);

        foreach (var item in Enumerable.Range(1000, HotCap))
        {
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        // 将项目从热到温循环,因为热尚未填充。
        // 跳过先前删除的项目 。
        _lru.WarmCount.Should().Be(HotCap - 1);
        _lru.ColdCount.Should().Be(0);
    }


    [Fact]
    public void WhenItemRemovedFromWarmAfterWarmupItIsEagerlyCycledOut()
    {
        for (var i = 0; i < _lru.Capacity; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);
        }

        Print();                                    // [6,7,8] 暖 [1,2,3] 冷 [0,4,5]
        _lru.Metrics.Evicted.Should().Be(0);

        _lru.TryRemove(1);

        _lru.GetOrAdd(6, _valueFactory.Create); // 6 -> W
        _lru.GetOrAdd(9, _valueFactory.Create);

        Print();                                    // [2,3,6] 冷[0,4,5]

        _lru.Metrics.Evicted.Should().Be(0);
    }

    [Fact]
    public void WhenItemRemovedFromColdAfterWarmupItIsEagerlyCycledOut()
    {
        for (var i = 0; i < _lru.Capacity; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);
        }

        Print();                                    // [6,7,8] 暖 [1,2,3] 冷 [0,4,5]
        _lru.Metrics.Evicted.Should().Be(0);

        _lru.GetOrAdd(0, _valueFactory.Create);
        _lru.TryRemove(0);

        _lru.GetOrAdd(9, _valueFactory.Create);

        Print();                                    // [1,2,3] 冷[4,5,6]

        _lru.Metrics.Evicted.Should().Be(0);
    }

    [Fact]
    public void WhenKeyDoesNotExistTryRemoveReturnsFalse()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryRemove(2).Should().BeFalse();
    }

    [Fact]
    public void WhenItemsAreRemovedTrimRemovesDeletedItemsFromQueues()
    {
        for (var i = 0; i < _lru.Capacity; i++)
        {
            _lru.GetOrAdd(i, _valueFactory.Create);
        }

        _lru.TryRemove(0);
        _lru.TryRemove(1);
        _lru.TryRemove(6);

        _lru.Trim(1);

        _lru.HotCount.Should().Be(HotCap);
        _lru.WarmCount.Should().Be(WarmCap);
        _lru.ColdCount.Should().Be(ColdCap - 1);
    }

    [Fact]
    public void WhenRepeatedlyAddingAndRemovingSameValueLruRemainsInConsistentState()
    {
        for (var i = 0; i < Capacity; i++)
        {
            // 因为 TryRemove 将项目留在队列中, 当它最终被删除时
            // 从冷列队列中删除, 它不应该删除新创建的值 。
            _lru.GetOrAdd(1, _valueFactory.Create);
            _lru.TryGet(1, out var value).Should().BeTrue();
            _lru.TryRemove(1);
        }
    }

    [Fact]
    public void WhenKeyExistsTryUpdateUpdatesValueAndReturnsTrue()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryUpdate(1, "2").Should().BeTrue();

        _lru.TryGet(1, out var value);
        value.Should().Be("2");
    }

    [Fact]
    public void WhenKeyExistsTryUpdateDisposesOldValue()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);
        var disposableValueFactory = new DisposableValueFactory();
        var newValue = new DisposableItem();

        lruOfDisposable.GetOrAdd(1, disposableValueFactory.Create);
        lruOfDisposable.TryUpdate(1, newValue);

        disposableValueFactory.Items[1].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void WhenKeyDoesNotExistTryUpdateReturnsFalse()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryUpdate(2, "3").Should().BeFalse();
    }

    // 反对的兼容: 删除条件汇编
#if NETCOREAPP3_0_OR_GREATER
    [Fact]
    public void WhenKeyExistsTryUpdateIncrementsUpdateCount()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryUpdate(1, "2").Should().BeTrue();

        _lru.Metrics.Updated.Should().Be(1);
    }

    [Fact]
    public void WhenKeyDoesNotExistTryUpdateDoesNotIncrementCounter()
    {
        _lru.GetOrAdd(1, _valueFactory.Create);

        _lru.TryUpdate(2, "3").Should().BeFalse();

        _lru.Metrics.Updated.Should().Be(0);
    }
#endif
    [Fact]
    public void WhenKeyDoesNotExistAddOrUpdateAddsNewItem()
    {
        _lru.AddOrUpdate(1, "1");

        _lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("1");
    }

    [Fact]
    public void WhenKeyExistsAddOrUpdateUpdatesExistingItem()
    {
        _lru.AddOrUpdate(1, "1");
        _lru.AddOrUpdate(1, "2");

        _lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("2");
    }

    [Fact]
    public async Task WhenItemExceedsTimeToLivePeriodicCleanupRemovesIt()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(1);
        lru.TryGet(1, out var value).Should().BeFalse();
        value.Should().BeNull();
        lru.Count.Should().Be(0);
        lru.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenItemAgeEqualsTimeToLivePeriodicCleanupKeepsItemUntilLaterTick()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var firstCleanup = await listener.WaitForCleanupAsync();
        firstCleanup.Should().Be(0);
        lru.Count.Should().Be(1);
        lru.Keys.Should().BeEquivalentTo([1]);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var secondCleanup = await listener.WaitForCleanupAsync();
        secondCleanup.Should().Be(1);
        lru.TryGet(1, out _).Should().BeFalse();
        lru.Count.Should().Be(0);
    }

    [Fact]
    public async Task WhenItemIsReadTimeToLiveIsExtended()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("1");

        timeProvider.Advance(TimeSpan.FromSeconds(45));

        var firstCleanup = await listener.WaitForCleanupAsync();
        firstCleanup.Should().Be(0);
        lru.TryGet(1, out value).Should().BeTrue();
        value.Should().Be("1");

        timeProvider.Advance(TimeSpan.FromSeconds(61));

        var secondCleanup = await listener.WaitForCleanupAsync();
        secondCleanup.Should().Be(1);
        lru.TryGet(1, out _).Should().BeFalse();
    }

    [Fact]
    public async Task WhenItemIsUpdatedTimeToLiveIsExtended()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        lru.TryUpdate(1, "2").Should().BeTrue();
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        var firstCleanup = await listener.WaitForCleanupAsync();
        firstCleanup.Should().Be(0);

        lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("2");
    }

    [Fact]
    public async Task WhenExpiredItemsAreCleanedUpTheyAreDisposed()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = new ConcurrentLruCache<int, DisposableItem>(
            capacity: 6,
            comparer: EqualityComparer<int>.Default,
            timeToLive: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);
        var items = Enumerable.Range(1, 2).Select(_ => new DisposableItem()).ToArray();

        lru.AddOrUpdate(1, items[0]);
        lru.AddOrUpdate(2, items[1]);
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var cleanup = await listener.WaitForCleanupAsync();

        cleanup.Should().Be(2);
        items.All(item => item.IsDisposed).Should().BeTrue();
        lru.Count.Should().Be(0);
    }

    [Fact]
    public async Task WhenExpiredItemDisposalThrowsExpirationLoopContinues()
    {
        var timeProvider = new FakeTimeProvider();
        var lru = new ConcurrentLruCache<int, ThrowingDisposableItem>(
            capacity: 6,
            comparer: EqualityComparer<int>.Default,
            timeToLive: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);
        var item = new ThrowingDisposableItem();

        try
        {
            lru.AddOrUpdate(1, item);
            timeProvider.Advance(TimeSpan.FromMinutes(2));

            await item.Disposed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            timeProvider.Advance(TimeSpan.FromMinutes(1));

            var cleanup = await listener.WaitForCleanupAsync();
            cleanup.Should().Be(0);

            Func<Task> dispose = async () => await lru.DisposeAsync();
            await dispose.Should().NotThrowAsync();
        }
        finally
        {
            await lru.DisposeAsync();
        }
    }

    [Fact]
    public async Task WhenOtherCacheExpiresCleanupListenerWithShortTimeoutDoesNotCompleteForNonTargetCache()
    {
        var targetTimeProvider = new FakeTimeProvider();
        var otherTimeProvider = new FakeTimeProvider();
        await using var targetLru = CreateExpiringLru(targetTimeProvider);
        await using var otherLru = CreateExpiringLru(otherTimeProvider);
        using var targetListener = new ConcurrentLruCacheExpirationCleanupListener(targetLru);
        using var otherListener = new ConcurrentLruCacheExpirationCleanupListener(otherLru);

        targetLru.AddOrUpdate(1, "target");
        otherLru.AddOrUpdate(1, "other");
        otherTimeProvider.Advance(TimeSpan.FromMinutes(2));

        var otherCleanup = await otherListener.WaitForCleanupAsync();
        otherCleanup.Should().Be(1);

        Func<Task> waitForTargetCleanup = () => targetListener.WaitForCleanupAsync(TimeSpan.FromMilliseconds(10));
        await waitForTargetCleanup.Should().ThrowAsync<TimeoutException>();

        using var targetListenerAfterNegativeAssertion = new ConcurrentLruCacheExpirationCleanupListener(targetLru);
        targetTimeProvider.Advance(TimeSpan.FromMinutes(2));

        var targetCleanup = await targetListenerAfterNegativeAssertion.WaitForCleanupAsync();
        targetCleanup.Should().Be(1);
    }

    [Fact]
    public async Task WhenItemsExceedTimeToLivePeriodicCleanupRemovesExpiredItemsFromAllQueues()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider, capacity: 9);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        lru.AddOrUpdate(2, "2");
        lru.AddOrUpdate(3, "3");
        lru.GetOrAdd(1, _valueFactory.Create);
        lru.GetOrAdd(2, _valueFactory.Create);
        lru.GetOrAdd(3, _valueFactory.Create);

        lru.AddOrUpdate(4, "4");
        lru.AddOrUpdate(5, "5");
        lru.AddOrUpdate(6, "6");

        lru.AddOrUpdate(7, "7");
        lru.AddOrUpdate(8, "8");
        lru.AddOrUpdate(9, "9");

        lru.HotCount.Should().BeGreaterThan(0);
        lru.WarmCount.Should().BeGreaterThan(0);
        lru.ColdCount.Should().BeGreaterThan(0);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(9);
        lru.Count.Should().Be(0);
        lru.HotCount.Should().Be(0);
        lru.WarmCount.Should().Be(0);
        lru.ColdCount.Should().Be(0);
    }

    [Fact]
    public async Task WhenCacheHasExpiredAndFreshItemsPeriodicCleanupRemovesOnlyExpiredItems()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider, capacity: 9);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        lru.AddOrUpdate(1, "1");
        lru.AddOrUpdate(2, "2");
        lru.AddOrUpdate(3, "3");

        lru.AddOrUpdate(4, "4");
        lru.AddOrUpdate(5, "5");
        lru.AddOrUpdate(6, "6");

        timeProvider.Advance(TimeSpan.FromSeconds(59));

        lru.GetOrAdd(1, _valueFactory.Create);
        lru.GetOrAdd(2, _valueFactory.Create);
        lru.GetOrAdd(3, _valueFactory.Create);

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(3);
        lru.Count.Should().Be(3);
        lru.Keys.Should().BeEquivalentTo([1, 2, 3]);

        var total = lru.HotCount + lru.WarmCount + lru.ColdCount;
        total.Should().Be(3);
    }

    [Fact]
    public async Task WhenItemIsInsertedBeforePeriodicCleanupRunsItIsNotRemoved()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.Advance(TimeSpan.FromHours(1));
        await using var lru = CreateExpiringLru(timeProvider);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);

        timeProvider.Advance(TimeSpan.FromSeconds(50));
        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(0);
        lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("1");
        lru.Count.Should().Be(1);
    }

    [Fact]
    public async Task WhenExpiredItemsAreCleanedUpCacheIsMarkedCold()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider, capacity: 9);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);
        var testAccessor = GetTestAccessor(lru);

        lru.AddOrUpdate(1, "1");
        lru.AddOrUpdate(2, "2");
        lru.AddOrUpdate(3, "3");
        lru.GetOrAdd(1, _valueFactory.Create);
        lru.GetOrAdd(2, _valueFactory.Create);
        lru.GetOrAdd(3, _valueFactory.Create);

        lru.AddOrUpdate(4, "4");
        lru.AddOrUpdate(5, "5");
        lru.AddOrUpdate(6, "6");

        lru.AddOrUpdate(7, "7");
        lru.AddOrUpdate(8, "8");
        lru.AddOrUpdate(9, "9");

        testAccessor.IsWarm.Should().BeTrue();

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(9);
        testAccessor.IsWarm.Should().BeFalse();

        for (var i = 0; i < lru.Capacity; i++)
        {
            lru.GetOrAdd(i, _valueFactory.Create);
        }

        lru.Count.Should().Be(lru.Capacity);

        var total = lru.HotCount + lru.WarmCount + lru.ColdCount;
        total.Should().Be(lru.Capacity);
    }

    [Fact]
    public async Task WhenItemsAreRemovedPeriodicCleanupRemovesDeletedItemsFromQueues()
    {
        var timeProvider = new FakeTimeProvider();
        await using var lru = CreateExpiringLru(timeProvider, capacity: 9);
        using var listener = new ConcurrentLruCacheExpirationCleanupListener(lru);
        var testAccessor = GetTestAccessor(lru);

        for (var i = 0; i < lru.Capacity; i++)
        {
            lru.GetOrAdd(i, _valueFactory.Create);
        }

        var hotCount = lru.HotCount;
        var warmCount = lru.WarmCount;
        var coldCount = lru.ColdCount;
        var hotKey = testAccessor.HotQueue.First().Key;
        var warmKey = testAccessor.WarmQueue.First().Key;
        var coldKey = testAccessor.ColdQueue.First().Key;

        lru.TryRemove(hotKey).Should().BeTrue();
        lru.TryRemove(warmKey).Should().BeTrue();
        lru.TryRemove(coldKey).Should().BeTrue();

        lru.HotCount.Should().Be(hotCount);
        lru.WarmCount.Should().Be(warmCount);
        lru.ColdCount.Should().Be(coldCount);

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var cleanup = await listener.WaitForCleanupAsync();
        cleanup.Should().Be(0);
        lru.Count.Should().Be(6);
        lru.HotCount.Should().Be(hotCount - 1);
        lru.WarmCount.Should().Be(warmCount - 1);
        lru.ColdCount.Should().Be(coldCount - 1);
    }

    [Fact]
    public void WhenTimeToLiveIsNotConfiguredItemsDoNotExpire()
    {
        var timeProvider = new FakeTimeProvider();
        var lru = new ConcurrentLruCache<int, string>(
            capacity: 6,
            comparer: EqualityComparer<int>.Default,
            timeProvider: timeProvider);

        lru.AddOrUpdate(1, "1");
        timeProvider.Advance(TimeSpan.FromDays(1));

        lru.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be("1");
    }

    [Fact]
    public void WhenTimeToLiveIsNotPositiveCtorThrows()
    {
        Action constructor = () => new ConcurrentLruCache<int, string>(6, TimeSpan.Zero);

        constructor.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenKeyExistsAddOrUpdateGuidUpdatesExistingItem()
    {
        var lru2 = new ConcurrentLruCache<int, Guid>(Capacity, EqualityComparer<int>.Default);

        var b = new byte[8];
        lru2.AddOrUpdate(1, new Guid(1, 0, 0, b));
        lru2.AddOrUpdate(1, new Guid(2, 0, 0, b));

        lru2.TryGet(1, out var value).Should().BeTrue();
        value.Should().Be(new Guid(2, 0, 0, b));
    }

    [Fact]
    public void WhenKeyExistsAddOrUpdateDisposesOldValue()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);
        var disposableValueFactory = new DisposableValueFactory();
        var newValue = new DisposableItem();

        lruOfDisposable.GetOrAdd(1, disposableValueFactory.Create);
        lruOfDisposable.AddOrUpdate(1, newValue);

        disposableValueFactory.Items[1].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void WhenKeyDoesNotExistAddOrUpdateMaintainsLruOrder()
    {
        AddOrUpdateRangeInclusive(1, Capacity + 1);

        _lru.HotCount.Should().Be(HotCap);
        _lru.WarmCount.Should().Be(WarmCap);
        _lru.TryGet(0, out _).Should().BeFalse();
    }

    [Fact]
    public void WhenCacheIsEmptyClearIsNoOp()
    {
        _lru.Clear();
        _lru.Count.Should().Be(0);
    }

    [Fact]
    public void WhenItemsExistClearRemovesAllItems()
    {
        _lru.AddOrUpdate(1, "1");
        _lru.AddOrUpdate(2, "2");

        _lru.Clear();

        _lru.Count.Should().Be(0);

        // 校验队列已被清除
        _lru.HotCount.Should().Be(0);
        _lru.WarmCount.Should().Be(0);
        _lru.ColdCount.Should().Be(0);
    }

    // 这是特殊情况:
    // 周期1:热度
    // 周期2:温暖
    // 周期3:温暖寒冷
    // 第 4 轮：冷却后移除
    // 周期5:冷
    [Fact]
    public void WhenCacheIsSize3ItemsExistAndItemsAccessedClearRemovesAllItems()
    {
        var lru = new ConcurrentLruCache<int, string>(3);

        lru.AddOrUpdate(1, "1");
        lru.AddOrUpdate(2, "1");

        lru.TryGet(1, out _);
        lru.TryGet(2, out _);

        lru.Clear();

        lru.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void WhenItemsExistAndItemsAccessedClearRemovesAllItems(int itemCount)
    {
        // 默认能力为9. 测试所有可能接触物品的状态
        // 隐藏处。

        for (var i = 0; i < itemCount; i++)
        {
            _lru.AddOrUpdate(i, "1");
        }

        // 至n项
        for (var i = 0; i < itemCount; i++)
        {
            _lru.TryGet(i, out _);
        }

        _lru.Clear();

        _testOutputHelper.WriteLine("LRU " + string.Join(" ", _lru.Keys));

        _lru.Count.Should().Be(0);

        // 校验队列已被清除
        _lru.HotCount.Should().Be(0);
        _lru.WarmCount.Should().Be(0);
        _lru.ColdCount.Should().Be(0);
    }

    [Fact]
    public void WhenWarmThenClearedIsWarmIsReset()
    {
        for (var i = 0; i < Capacity; i++)
        {
            Touch(i);
        }

        var testAccessor = GetTestAccessor(_lru);
        testAccessor.IsWarm.Should().BeTrue();

        _lru.Clear();
        _lru.Count.Should().Be(0);
        testAccessor.IsWarm.Should().BeFalse();

        for (var i = 0; i < Capacity; i++)
        {
            Touch(i);
        }

        testAccessor.IsWarm.Should().BeTrue();
        _lru.Count.Should().Be(_lru.Capacity);
    }

    [Fact]
    public void WhenWarmThenTrimIsWarmIsReset()
    {
        GetOrAddRangeInclusive(1, Capacity);

        var testAccessor = GetTestAccessor(_lru);
        testAccessor.IsWarm.Should().BeTrue();
        _lru.Trim(Capacity / 2);

        testAccessor.IsWarm.Should().BeFalse();
        _lru.Count.Should().Be(Capacity / 2);

        for (var i = 0; i < Capacity; i++)
        {
            Touch(i);
        }

        testAccessor.IsWarm.Should().BeTrue();
        _lru.Count.Should().Be(_lru.Capacity);
    }

    /// <summary>
    /// 证明缓存妥善处置了通过Clear () 移除的可处置物品。
    /// 这对于防止在缓存可支配物体时出现资源泄漏十分重要。
    /// </summary>
    [Fact]
    public void WhenItemsAreDisposableClearDisposesItemsOnRemove()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);

        var items = Enumerable.Range(1, 4).Select(i => new DisposableItem()).ToList();

        for (var i = 0; i < 4; i++)
        {
            lruOfDisposable.AddOrUpdate(i, items[i]);
        }

        lruOfDisposable.Clear();

        items.All(i => i.IsDisposed == true).Should().BeTrue();
    }

    [Fact]
    public void WhenTrimCountIsZeroThrows()
    {
        _lru.Invoking(l => _lru.Trim(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenTrimCountIsMoreThanCapacityThrows()
    {
        _lru.Invoking(l => _lru.Trim(HotCap + WarmCap + ColdCap + 1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(80)]
    [InlineData(90)]
    public void WhenColdItemsExistTrimRemovesExpectedItemCount(int trimCount)
    {
        FillCache();

        // 暖热物品
        var warmItems = Enumerable.Range(1, WarmCap).ToArray();
        foreach (var item in warmItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        // 冷藏物品(添加但未动)
        var coldItems = Enumerable.Range(1000, ColdCap).ToArray();
        foreach (var item in coldItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
        }

        // 热物品( 显示先前的热物品冷到冷)
        var hotItems = Enumerable.Range(2000, HotCap).ToArray();
        foreach (var item in hotItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
        }

        _lru.Trim(trimCount);

        int[] expected = [
            .. warmItems.Skip(Math.Max(0, trimCount - coldItems.Length)),
            .. hotItems,
            .. coldItems.Skip(trimCount)];
        _lru.Keys.Order().Should().BeEquivalentTo(expected.Order());
    }

    [Theory]
    [InlineData(1, new[] { 6, 5, 4, 3, 2 })]
    [InlineData(2, new[] { 6, 5, 4, 3 })]
    [InlineData(3, new[] { 6, 5, 4 })]
    [InlineData(4, new[] { 6, 5 })]
    [InlineData(5, new[] { 6 })]
    [InlineData(6, new int[] { })]
    [InlineData(7, new int[] { })]
    [InlineData(8, new int[] { })]
    [InlineData(9, new int[] { })]
    public void WhenHotAndWarmItemsExistTrimRemovesExpectedItemCount(int itemCount, int[] expected)
    {
        // 初始状态 :
        // 热度=6,5,4
        // 热量=3,2,1
        // 冷= -
        _lru.AddOrUpdate(1, "1");
        _lru.AddOrUpdate(2, "2");
        _lru.AddOrUpdate(3, "3");
        _lru.GetOrAdd(1, i => i.ToString());
        _lru.GetOrAdd(2, i => i.ToString());
        _lru.GetOrAdd(3, i => i.ToString());

        _lru.AddOrUpdate(4, "4");
        _lru.AddOrUpdate(5, "5");
        _lru.AddOrUpdate(6, "6");

        _lru.Trim(itemCount);

        _lru.Keys.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(1, new[] { 3, 2 })]
    [InlineData(2, new[] { 3 })]
    [InlineData(3, new int[] { })]
    [InlineData(4, new int[] { })]
    [InlineData(5, new int[] { })]
    [InlineData(6, new int[] { })]
    [InlineData(7, new int[] { })]
    [InlineData(8, new int[] { })]
    [InlineData(9, new int[] { })]
    public void WhenHotItemsExistTrimRemovesExpectedItemCount(int itemCount, int[] expected)
    {
        // 初始状态 :
        // 热热=3 2 1
        // 热度=-
        // 冷= -
        _lru.AddOrUpdate(1, "1");
        _lru.AddOrUpdate(2, "2");
        _lru.AddOrUpdate(3, "3");

        _lru.Trim(itemCount);

        _lru.Keys.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(80)]
    public void WhenColdItemsAreTouchedTrimRemovesExpectedItemCount(int trimCount)
    {
        FillCache();

        // 暖热物品
        var warmItems = Enumerable.Range(1, WarmCap).ToArray();
        foreach (var item in warmItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        // 冷藏物品(添加但未动)
        var coldItems = Enumerable.Range(1000, ColdCap).ToArray();
        foreach (var item in coldItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
        }

        // 热物品( 显示先前的热物品冷到冷)
        var hotItems = Enumerable.Range(2000, HotCap).ToArray();
        foreach (var item in hotItems)
        {
            _lru.AddOrUpdate(item, item.ToString());
        }

        // 触碰冷冻物品,以促其取暖
        foreach (var item in coldItems)
        {
            _lru.GetOrAdd(item, _valueFactory.Create);
        }

        _lru.Trim(trimCount);

        int[] expected = [
            .. warmItems.Skip(Math.Max(0, trimCount)),
            .. hotItems,
            .. coldItems];
        _lru.Keys.Order().Should().BeEquivalentTo(expected.Order());
        _testOutputHelper.WriteLine("LRU " + string.Join(" ", _lru.Keys));
        _testOutputHelper.WriteLine("exp " + string.Join(" ", expected));

        _lru.Keys.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void WhenItemsExistAndItemsAccessedTrimRemovesAllItems(int itemCount)
    {
        // 默认能力为9. 测试所有可能接触物品的状态
        // 隐藏处。

        for (var i = 0; i < itemCount; i++)
        {
            _lru.AddOrUpdate(i, "1");
        }

        // 至n项
        for (var i = 0; i < itemCount; i++)
        {
            _lru.TryGet(i, out _);
        }

        _lru.Trim(Math.Min(itemCount, _lru.Capacity));

        _testOutputHelper.WriteLine("LRU " + string.Join(" ", _lru.Keys));

        _lru.Count.Should().Be(0);

        // 校验队列已被清除
        _lru.HotCount.Should().Be(0);
        _lru.WarmCount.Should().Be(0);
        _lru.ColdCount.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void WhenItemsRemovedClearRemovesAllItems(int itemCount)
    {
        for (var i = 0; i < itemCount; i++)
        {
            _lru.AddOrUpdate(i, "1");
        }

        // 中,此选项在队列中留下一个项,但不会出现在字典中
        _lru.TryRemove(0, out _);

        _lru.Clear();

        _testOutputHelper.WriteLine("LRU " + string.Join(" ", _lru.Keys));

        _lru.Count.Should().Be(0);

        // 校验队列已被清除
        _lru.HotCount.Should().Be(0);
        _lru.WarmCount.Should().Be(0);
        _lru.ColdCount.Should().Be(0);
    }

    [Fact]
    public void WhenItemsAreDisposableTrimDisposesItems()
    {
        var lruOfDisposable = new ConcurrentLruCache<int, DisposableItem>(6, EqualityComparer<int>.Default);

        var items = Enumerable.Range(1, 4).Select(i => new DisposableItem()).ToList();

        for (var i = 0; i < 4; i++)
        {
            lruOfDisposable.AddOrUpdate(i, items[i]);
        }

        lruOfDisposable.Trim(2);

        items[0].IsDisposed.Should().BeTrue();
        items[1].IsDisposed.Should().BeTrue();
        items[2].IsDisposed.Should().BeFalse();
        items[3].IsDisposed.Should().BeFalse();
    }

    private void FillCache() => GetOrAddRangeInclusive(-1, -Capacity);

    private static ConcurrentLruCache<int, string> CreateExpiringLru(TimeProvider timeProvider, int capacity = 6) =>
        new(
            capacity: capacity,
            comparer: EqualityComparer<int>.Default,
            timeToLive: TimeSpan.FromMinutes(1),
            timeProvider: timeProvider);

    private void Print()
    {
#if DEBUG
        _testOutputHelper.WriteLine(_lru.FormatLruString());
#endif
    }

    private class ValueFactory
    {
        public int TimesCalled;

        public string Create(int key)
        {
            TimesCalled++;
            return key.ToString();
        }

        public string Create<TArg>(int key, TArg arg)
        {
            TimesCalled++;
            return $"{key}{arg}";
        }

        public Task<string> CreateAsync(int key)
        {
            TimesCalled++;
            return Task.FromResult(key.ToString());
        }

        public Task<string> CreateAsync<TArg>(int key, TArg arg)
        {
            TimesCalled++;
            return Task.FromResult($"{key}{arg}");
        }
    }

    private class DisposableItem : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ThrowingDisposableItem : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public void Dispose()
        {
            _disposed.TrySetResult();
            throw new InvalidOperationException("Dispose failed");
        }
    }

    private class DisposableValueFactory
    {
        public Dictionary<int, DisposableItem> Items { get; } = [];

        public DisposableItem Create(int key)
        {
            var item = new DisposableItem();
            Items.Add(key, item);
            return item;
        }

        public Task<DisposableItem> CreateAsync(int key)
        {
            var item = new DisposableItem();
            Items.Add(key, item);
            return Task.FromResult(item);
        }
    }
}

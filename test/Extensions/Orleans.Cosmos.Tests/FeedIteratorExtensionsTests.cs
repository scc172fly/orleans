using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Orleans.Reminders.Cosmos;
using Xunit;

namespace Tester.Cosmos.Reminders;

/// <summary>
/// <see cref="FeedIteratorExtensions.ToListAsync{T}"/>单位测试。这些测试验证
/// 具体变异的细微变形, 一旦在
/// <c>CosmosReminderTable.ReadRows</c>: <see cref="FeedIterator{T}"/> 可返回
/// <see cref="FeedIterator.HasMoreResults"/> 仍然为 <c>true</c>时空页面
/// (e.g.)。当上一页在扫描分区时用完RU预算时,
/// 排水工具必须保持在空页面的重复。
///
/// 实时宇宙 DB (或模拟器) 无法可靠地复制此模式
/// 以确定性的方式, 所以这些测试驱动辅助器 用记忆驱动
/// 播放脚本序列的<see cref="FeedIterator{T}"/> 子类
/// 包括空页。
/// </summary>
public class FeedIteratorExtensionsTests
{
    [Fact]
    public async Task ToListAsync_EmptyPageInMiddle_ContinuesIterating()
    {
        // 模拟病理页面布局 修复卫兵对照: 结果,
        // 然后是空页面,而 HasMoreResults 仍然真实,然后是更多结果。
        // "破开第一页空纸"排水道会让后行倒下
        var iterator = new FakeFeedIterator<int>(
            new[] { 1, 2, 3 },
            Array.Empty<int>(),
            new[] { 4, 5 });

        var drained = await iterator.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, drained);
    }

    [Fact]
    public async Task ToListAsync_LeadingEmptyPage_ContinuesIterating()
    {
        // 首页空空, HasMoreResults 事实依然如此。
        // 即使有更进一步匹配,实施时将返回零行。
        var iterator = new FakeFeedIterator<int>(
            Array.Empty<int>(),
            new[] { 1, 2, 3 });

        var drained = await iterator.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1, 2, 3 }, drained);
    }

    [Fact]
    public async Task ToListAsync_AllEmptyPages_ReturnsEmpty()
    {
        var iterator = new FakeFeedIterator<int>(
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>());

        var drained = await iterator.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(drained);
    }

    [Fact]
    public async Task ToListAsync_SinglePage_ReturnsAllItems()
    {
        var iterator = new FakeFeedIterator<int>(new[] { 1, 2, 3, 4, 5 });

        var drained = await iterator.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, drained);
    }

    [Fact]
    public async Task ToListAsync_TrailingEmptyPage_ReturnsAllItems()
    {
        var iterator = new FakeFeedIterator<int>(
            new[] { 1, 2 },
            Array.Empty<int>());

        var drained = await iterator.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1, 2 }, drained);
    }

    /// <summary>
    /// 播放脚本页面列表的 <see cref="FeedIterator{T}"/> 。
    /// <see cref="HasMoreResults"/> 保持真实的 <c>true</c> ,直到每个脚本页面
    /// 以 <see cref="ReadNextAsync(CancellationToken)"/>方式消耗,所以空
    /// 页面不会自动终止迭代。
    /// </summary>
    private sealed class FakeFeedIterator<T> : FeedIterator<T>
    {
        private readonly Queue<IReadOnlyList<T>> _pages;

        public FakeFeedIterator(params IReadOnlyList<T>[] pages)
        {
            _pages = new Queue<IReadOnlyList<T>>(pages);
        }

        public override bool HasMoreResults => _pages.Count > 0;

        public override Task<FeedResponse<T>> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            if (_pages.Count == 0)
            {
                throw new InvalidOperationException("ReadNextAsync called after all pages consumed.");
            }

            return Task.FromResult<FeedResponse<T>>(new FakeFeedResponse<T>(_pages.Dequeue()));
        }
    }

    /// <summary>
    /// 将成员排水沟暴露在下水道的最小 <see cref="FeedResponse{T}"/> 根管上
    /// 帮助符实际上读取 (<see cref="Count"/> 和计算器) 。
    /// 其它返回安全默认,所以不丢弃非关联访问。
    /// </summary>
    private sealed class FakeFeedResponse<T> : FeedResponse<T>
    {
        private readonly IReadOnlyList<T> _items;

        public FakeFeedResponse(IReadOnlyList<T> items) => _items = items;

        public override int Count => _items.Count;
        public override string ContinuationToken => null!;
        public override string IndexMetrics => null!;
        public override Headers Headers => null!;
        public override IEnumerable<T> Resource => _items;
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
        public override double RequestCharge => 0;
        public override string ActivityId => string.Empty;
        public override string ETag => null!;
        public override CosmosDiagnostics Diagnostics => null!;

        public override IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    }
}

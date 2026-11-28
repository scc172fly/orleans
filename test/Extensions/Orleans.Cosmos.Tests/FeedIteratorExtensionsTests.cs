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
/// <see cref="FeedIteratorExtensions.ToListAsync{T}"/> 的单元测试。验证以下
/// 特定的分页不变量，它曾在生产环境中触发
/// <c>CosmosReminderTable.ReadRows</c> 的实际缺陷：<see cref="FeedIterator{T}"/> 可能返回
/// 空页，而 <see cref="FeedIterator.HasMoreResults"/> 仍为 <c>true</c>
/// （例如，上一页在扫描一个没有匹配项的分区时耗尽 RU 预算）。
/// 用于读取全部结果的辅助方法必须跳过空页并继续迭代。
///
/// 运行中的 Cosmos DB（或模拟器）无法以确定性方式可靠地复现这种模式，
/// 因此这些测试使用内存中的
/// <see cref="FeedIterator{T}"/> 子类，按预设顺序返回
/// 包含空页在内的各页结果，以驱动该辅助方法。
/// </summary>
public class FeedIteratorExtensionsTests
{
    [Fact]
    public async Task ToListAsync_EmptyPageInMiddle_ContinuesIterating()
    {
        // 模拟此次修复所防范的异常分页布局：先有结果，
        // 再出现 HasMoreResults 仍为 true 的空页，随后还有更多结果。
        // "遇到第一个空页就退出"的读取方式会丢弃后续行。
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
        // 第一页为空，但 HasMoreResults 仍为 true。遇空页即退出的
        // 实现会返回零行，即使后续页还存在匹配项。
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
    /// 按预设列表依次返回各页的 <see cref="FeedIterator{T}"/>。
    /// <see cref="HasMoreResults"/> 一直保持 <c>true</c>，直到所有预设页均已
    /// 通过 <see cref="ReadNextAsync(CancellationToken)"/> 读取，因此空页
    /// 本身绝不会终止迭代。
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
    /// 最小化的 <see cref="FeedResponse{T}"/> 桩对象，仅公开读取全部结果的
    /// 辅助方法实际读取的成员（<see cref="Count"/> 和枚举器）。其他成员
    /// 均返回安全的默认值，因此访问无关成员时不会抛出异常。
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

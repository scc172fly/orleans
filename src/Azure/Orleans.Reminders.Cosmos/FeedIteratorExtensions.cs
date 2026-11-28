using System.Threading;

namespace Orleans.Reminders.Cosmos;

internal static class FeedIteratorExtensions
{
    /// <summary>
    /// 完整读取 Cosmos DB <see cref="FeedIterator{T}"/>，收集
    /// 所有页中的全部项。跳过空页，但不会终止迭代：<see
    /// cref="FeedIterator.HasMoreResults"/> 可能仍为 <c>true</c>，即使刚才的
    /// <see cref="FeedIterator{T}.ReadNextAsync(System.Threading.CancellationToken)"/>
    /// 返回了空结果（例如上一页在扫描
    /// 无匹配项的分区时耗尽 RU 预算），因此必须继续迭代，直到
    /// <c>HasMoreResults</c> 为 <c>false</c>。
    /// </summary>
    public static async Task<List<T>> ToListAsync<T>(
        this FeedIterator<T> iterator,
        CancellationToken cancellationToken = default)
    {
        var items = new List<T>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            if (page is { Count: > 0 })
            {
                items.AddRange(page);
            }
        }

        return items;
    }
}

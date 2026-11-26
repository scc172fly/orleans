using System.Threading;

namespace Orleans.Reminders.Cosmos;

internal static class FeedIteratorExtensions
{
    /// <summary>
    /// 全部排出一个宇宙DB <see cref="FeedIterator{T}"/>,收集每个物品
    /// 全部页面。 空页面被跳过, 但不会终止迭代 : &lt; see
    /// cref="FeedIterator.HasMoreResults"/ > 可能留下来 <c>true</c> 空的
    /// <see cref="FeedIterator{T}.ReadNextAsync(System.Threading.CancellationToken)"/>
    /// 键,当前一页在扫描 a 时消耗 RU 预算时
    /// 没有匹配项目的分区分区),因此迭代必须持续到
    /// <c>HasMoreResults</c> 是 <c>false</c>.
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

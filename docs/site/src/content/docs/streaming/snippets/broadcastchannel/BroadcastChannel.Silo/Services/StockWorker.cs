// <broadcast_channel_stock_worker>
using System.Diagnostics;
using BroadcastChannel.GrainInterfaces;
using Microsoft.Extensions.Hosting;
using Orleans.BroadcastChannel;

namespace BroadcastChannel.Silo.Services;

internal sealed class StockWorker : BackgroundService
{
    private readonly StockClient _stockClient;
    private readonly IBroadcastChannelProvider _provider;
    private readonly List<StockSymbol> _symbols = Enum.GetValues<StockSymbol>().ToList();

    public StockWorker(
        StockClient stockClient, IClusterClient clusterClient) =>
        (_stockClient, _provider) =
        (stockClient, clusterClient.GetBroadcastChannelProvider(ChannelNames.LiveStockTicker));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 捕获起始时间戳 。
            long startingTimestamp = Stopwatch.GetTimestamp();

            // 获取所有更新的股票价值。
            Stock[] stocks = await Task.WhenAll(
                tasks: _symbols.Select(selector: _stockClient.GetStockAsync));

            // 获取实时库存心室广播频道。
            ChannelId channelId = ChannelId.Create(ChannelNames.LiveStockTicker, Guid.Empty);
            IBroadcastChannelWriter<Stock> channelWriter = _provider.GetChannelWriter<Stock>(channelId);

            // 播放此频道上的所有库存更新信息 。
            await Task.WhenAll(
                stocks.Where(s => s is not null).Select(channelWriter.Publish));

            // 使用已过的时间计算15秒的延迟。
            int elapsed = Stopwatch.GetElapsedTime(startingTimestamp).Milliseconds;
            int remaining = Math.Max(0, 15_000 - elapsed);

            await Task.Delay(remaining, stoppingToken);
        }
    }
}
// </broadcast_channel_stock_worker>

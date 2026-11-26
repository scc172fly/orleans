using Common;
using GrainInterfaces;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Grains;

// ImplicitStreamSubscription 属性在此属性是默认于内的所有流
// 指定命名空间: 当某些数据被推到命名空间流 Constants.StreamNamespace时,
// a 类型 grain 的 ConsumerGrain ,与流的首饰相同,将接收电文。
// 即使目前不存在 grain 的激活,运行时间也会自动
// 创建一个新的,并向它发送信息。
[ImplicitStreamSubscription(Constants.StreamNamespace)]
public class ConsumerGrain : Grain, IConsumerGrain, IStreamSubscriptionObserver
{
    private readonly ILogger<IConsumerGrain> _logger;
    private readonly LoggerObserver _observer;

    /// <summary>
    /// 将记录流活动类
    /// </summary>
    private class LoggerObserver : IAsyncObserver<int>
    {
        private readonly ILogger<IConsumerGrain> _logger;

        public LoggerObserver(ILogger<IConsumerGrain> logger)
        {
            _logger = logger;
        }

        public Task OnCompletedAsync()
        {
            _logger.LogInformation("OnCompletedAsync");
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.LogInformation("OnErrorAsync: {Exception}", ex);
            return Task.CompletedTask;
        }

        public Task OnNextAsync(int item, StreamSequenceToken? token = null)
        {
            _logger.LogInformation("OnNextAsync: item: {Item}, token = {Token}", item, token);
            return Task.CompletedTask;
        }
    }

    public ConsumerGrain(ILogger<IConsumerGrain> logger)
    {
        _logger = logger;
        _observer = new LoggerObserver(_logger);
    }

    // 添加订阅时呼叫
    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        // 将我们 LoggerObserver 插插到流
        var handle = handleFactory.Create<int>();
        await handle.ResumeAsync(_observer);
    }


    public override Task OnActivateAsync(CancellationToken token)
    {
        _logger.LogInformation("OnActivateAsync");
        return Task.CompletedTask;
    }
}

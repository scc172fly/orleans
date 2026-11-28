using Common;
using GrainInterfaces;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Grains;

// 此处的 ImplicitStreamSubscription 特性用于隐式订阅
// 指定命名空间中的所有流：只要有数据推送到命名空间 Constants.StreamNamespace 的流，
// 与该流具有相同 GUID 的 ConsumerGrain 类型的 Grain 就会接收消息。
// 即使此 Grain 当前不存在激活实例，运行时也会自动
// 创建一个激活实例并向其发送消息。
[ImplicitStreamSubscription(Constants.StreamNamespace)]
public class ConsumerGrain : Grain, IConsumerGrain, IStreamSubscriptionObserver
{
    private readonly ILogger<IConsumerGrain> _logger;
    private readonly LoggerObserver _observer;

    /// <summary>
    /// 记录流事件的类。
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

    // 添加订阅时调用。
    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        // 将 LoggerObserver 接入流。
        var handle = handleFactory.Create<int>();
        await handle.ResumeAsync(_observer);
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OnActivateAsync");
        return Task.CompletedTask;
    }
}

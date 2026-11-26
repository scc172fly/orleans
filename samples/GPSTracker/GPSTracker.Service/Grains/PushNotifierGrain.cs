using GPSTracker.Common;
using GPSTracker.GrainInterface;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace GPSTracker.GrainImplementation;

[Reentrant]
[StatelessWorker(maxLocalWorkers: 12)]
public class PushNotifierGrain : Grain, IPushNotifierGrain
{
    private readonly Queue<VelocityMessage> _messageQueue = new();
    private readonly ILogger<PushNotifierGrain> _logger;
    private List<(SiloAddress Host, IRemoteLocationHub Hub)> _hubs = new();
    public PushNotifierGrain(ILogger<PushNotifierGrain> logger) => _logger = logger;
    private Task _flushTask = Task.CompletedTask;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 设置一个定时器, 定期冲红信件队列
        this.RegisterGrainTimer(
            _ =>
            {
                Flush();
                return Task.CompletedTask;
            },
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(15),
                Period = TimeSpan.FromMilliseconds(15),
                Interleave = true
            });

        // 设置定期更新枢纽的定时器,以应对“暴风”基础设施的变化
        await RefreshHubs();
        this.RegisterGrainTimer(
            async _ => await RefreshHubs(),
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(60),
                Period = TimeSpan.FromSeconds(60),
                Interleave = true
            });

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason deactivationReason, CancellationToken cancellationToken)
    {
        await Flush();
        await base.OnDeactivateAsync(deactivationReason, cancellationToken);
    }

    private async ValueTask RefreshHubs()
    {
        // 发现当前基础设施
        IHubListGrain hubListGrain = GrainFactory.GetGrain<IHubListGrain>(Guid.Empty);
        _hubs = await hubListGrain.GetHubs();
    }

    public ValueTask SendMessage(VelocityMessage message)
    {
        // 向发送队列添加消息
        _messageQueue.Enqueue(message);
        return new(Flush());
    }

    private Task Flush()
    {
        if (_flushTask.IsCompleted)
        {
            _flushTask = FlushInternal();
        }

        return _flushTask;

        async Task FlushInternal()
        {
            const int MaxMessagesPerBatch = 100;
            if (_messageQueue.Count == 0) return;

            while (_messageQueue.Count > 0)
            {
                // 将所有信件发送到全部 SignalR 中心
                var messagesToSend = new List<VelocityMessage>(Math.Min(_messageQueue.Count, MaxMessagesPerBatch));
                while (messagesToSend.Count < MaxMessagesPerBatch && _messageQueue.TryDequeue(out VelocityMessage? msg)) messagesToSend.Add(msg);

                var tasks = new List<Task>(_hubs.Count);
                var batch = new VelocityBatch(messagesToSend);

                foreach ((SiloAddress Host, IRemoteLocationHub Hub) hub in _hubs)
                {
                    tasks.Add(BroadcastUpdates(hub.Host, hub.Hub, batch, _logger));
                }

                await Task.WhenAll(tasks);
            }
        }
    }

    private static async Task BroadcastUpdates(SiloAddress host, IRemoteLocationHub hub, VelocityBatch batch, ILogger logger)
    {
        try
        {
            await hub.BroadcastUpdates(batch);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error broadcasting to host {Host}", host);
        }
    }
}

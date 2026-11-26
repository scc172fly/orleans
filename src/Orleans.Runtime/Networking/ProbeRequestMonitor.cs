using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// 监测收到集束弹药健康调查请求
    /// </summary>
    internal sealed class ProbeRequestMonitor
    {
        private readonly Lock _lock = new();
        private readonly TimeProvider _timeProvider;
        private long? _lastProbeRequestTimestamp;

        public ProbeRequestMonitor([FromKeyedServices(TimeProviderNames.Membership)] TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// 当此 silo 收到健康调查请求时呼叫。
        /// </summary>
        public void OnReceivedProbeRequest()
        {
            lock (_lock)
            {
                _lastProbeRequestTimestamp = _timeProvider.GetTimestamp();
            }
        }

        /// <summary>
        /// 自最近收到健康调查请求以来,期限已经过去。
        /// </summary>
        public TimeSpan? ElapsedSinceLastProbeRequest
        {
            get
            {
                lock (_lock)
                {
                    return _lastProbeRequestTimestamp is { } timestamp ? _timeProvider.GetElapsedTime(timestamp) : null;
                }
            }
        }
    }
}

using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Runtime.Messaging
{
    /// <summary>
    /// 监控入站集群健康探测请求。
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
        /// 此 Silo 收到健康探测请求时调用。
        /// </summary>
        public void OnReceivedProbeRequest()
        {
            lock (_lock)
            {
                _lastProbeRequestTimestamp = _timeProvider.GetTimestamp();
            }
        }

        /// <summary>
        /// 自最近一次收到健康探测请求以来经过的时长。
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

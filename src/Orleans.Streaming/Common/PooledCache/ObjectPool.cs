
using System;
using System.Threading;
using System.Collections.Concurrent;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 使用栈存储可用对象的简单对象池。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObjectPool<T> : IObjectPool<T>
        where T : PooledResource<T>
    {
        private const int DefaultPoolCapacity = 1 << 10; // 1k
        private readonly ConcurrentStack<T> pool;
        private readonly Func<T> factoryFunc;
        private long totalObjects;

        /// <summary>
        /// 用于报告当前对象池统计信息的监视器。
        /// </summary>
        private readonly IObjectPoolMonitor? monitor;
        private readonly PeriodicAction? periodicMonitoring;

        /// <summary>
        /// 简单对象池。
        /// </summary>
        /// <param name="factoryFunc">用于创建 T 类型新资源的函数。</param>
        /// <param name="monitor">用于报告对象池统计信息的监视器。</param>
        /// <param name="monitorWriteInterval"></param>
        public ObjectPool(Func<T> factoryFunc, IObjectPoolMonitor? monitor = null, TimeSpan? monitorWriteInterval = null)
        {
            if (factoryFunc == null)
            {
                throw new ArgumentNullException(nameof(factoryFunc));
            }

            this.factoryFunc = factoryFunc;
            pool = new ConcurrentStack<T>();

            // 监控
            this.monitor = monitor;
            if (this.monitor != null && monitorWriteInterval.HasValue)
            {
                this.periodicMonitoring = new PeriodicAction(monitorWriteInterval.Value, this.ReportObjectPoolStatistics);
            }

            this.totalObjects = 0;
        }

        /// <summary>
        /// 分配池化资源。
        /// </summary>
        /// <returns></returns>
        public virtual T Allocate()
        {
            // 如果无法从池中弹出资源，则使用池外部提供的 factoryFunc 创建新资源。
            if (!pool.TryPop(out var resource))
            {
                resource = factoryFunc();
                Interlocked.Increment(ref this.totalObjects);
            }
            this.monitor?.TrackObjectAllocated();
            this.periodicMonitoring?.TryAction(DateTime.UtcNow);
            resource.Pool = this;
            return resource;
        }

        /// <summary>
        /// 将资源归还给池。
        /// </summary>
        /// <param name="resource"></param>
        public virtual void Free(T resource)
        {
            this.monitor?.TrackObjectReleased();
            this.periodicMonitoring?.TryAction(DateTime.UtcNow);
            pool.Push(resource);
        }

        private void ReportObjectPoolStatistics()
        {
            var availableObjects = this.pool.Count;
            long claimedObjects = this.totalObjects - availableObjects;
            this.monitor!.Report(this.totalObjects, availableObjects, claimedObjects); // 仅通过 periodicMonitoring 调用；仅在 monitor 不为 null 时设置它（参见构造函数）。
        }
    }
}

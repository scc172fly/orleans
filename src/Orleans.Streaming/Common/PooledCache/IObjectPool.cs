
using System;
using System.Threading;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 简单对象池接口。
    /// 分配的对象应在释放时归还给池。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IObjectPool<T>
        where T : IDisposable
    {
        /// <summary>
        /// 分配池化资源。
        /// </summary>
        /// <returns></returns>
        T Allocate();

        /// <summary>
        /// 将资源归还给池。
        /// </summary>
        /// <param name="resource"></param>
        void Free(T resource);
    }

    /// <summary>
    /// 支持池化对象的实用工具类，使对象能够跟踪其来源池，并在释放时归还给该池。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class PooledResource<T> : IDisposable
        where T : PooledResource<T>, IDisposable
    {
        private IObjectPool<T>? pool;

        /// <summary>
        /// 获取释放此资源时应将其归还到的池。
        /// 池必须在分配资源时设置此属性。
        /// </summary>
        public IObjectPool<T> Pool { set { pool = value; } }

        /// <summary>
        /// 如果此对象用于固定大小的对象池，则应重写此方法，
        /// 使用将对象归还给池的清除实现。
        /// </summary>
        public virtual void SignalPurge()
        {
            Dispose();
        }

        /// <summary>
        /// 将数据项归还给池。
        /// </summary>
        public void Dispose()
        {
            IObjectPool<T>? localPool = Interlocked.Exchange(ref pool, null);
            if (localPool != null)
            {
                OnResetState();
                localPool.Free((T)this);
            }
        }

        /// <summary>
        /// 通知对象已被清除，以便将自身重置为
        /// 刚分配时的状态。
        /// </summary>
        public virtual void OnResetState()
        {
        }
    }
}

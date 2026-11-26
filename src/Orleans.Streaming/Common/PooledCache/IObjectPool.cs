
using System;
using System.Threading;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// 简单对象池界面
    /// 所分配的物品在处置后应归还池内。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IObjectPool<T>
        where T : IDisposable
    {
        /// <summary>
        /// 分配集合资源
        /// </summary>
        /// <returns></returns>
        T Allocate();

        /// <summary>
        /// 返回集合资源
        /// </summary>
        /// <param name="resource"></param>
        void Free(T resource);
    }

    /// <summary>
    /// 支持集合目标的实用性类别,允许它们跟踪其来自池池的跟踪,并在处置后返回池
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class PooledResource<T> : IDisposable
        where T : PooledResource<T>, IDisposable
    {
        private IObjectPool<T>? pool;

        /// <summary>
        /// 使池子在处置后将资源归还给处理。
        /// 集合体必须按资源分配情况确定这一财产。
        /// </summary>
        public IObjectPool<T> Pool { set { pool = value; } }

        /// <summary>
        /// 如果该对象用于固定大小的物体池,此调用应
        ///   被将对象返回池的清洗执行所取代 。
        /// </summary>
        public virtual void SignalPurge()
        {
            Dispose();
        }

        /// <summary>
        /// 返回集合项 。
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
        /// 通知对象已被清洗,以便重新设置
        ///   新分配对象的状态。
        /// </summary>
        public virtual void OnResetState()
        {
        }
    }
}

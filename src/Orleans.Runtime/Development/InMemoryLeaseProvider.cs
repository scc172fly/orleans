using Orleans.LeaseProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.Development
{
    /// <summary>
    /// 供开发及测试使用的内存租约提供程序。
    /// 此提供程序在内存中存储租约信息；如果 Grain
    /// 变为非活动状态，或 Silo 崩溃，则信息可能丢失。此实现仅适用于
    /// 测试或本地开发用途，不得用于生产环境。
    /// </summary>
    public class InMemoryLeaseProvider : ILeaseProvider
    {
        private readonly IDevelopmentLeaseProviderGrain leaseProvider;

        /// <summary>
        /// 初始化 <see cref="InMemoryLeaseProvider"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        public InMemoryLeaseProvider(IGrainFactory grainFactory)
        {
            this.leaseProvider = GetLeaseProviderGrain(grainFactory);
        }

        /// <inheritdoc/>
        public async Task<AcquireLeaseResult[]> Acquire(string category, LeaseRequest[] leaseRequests)
        {
            try
            {
                return await this.leaseProvider.Acquire(category, leaseRequests);
            }
            catch (Exception ex)
            {
                return leaseRequests.Select(request => new AcquireLeaseResult(new AcquiredLease(request.ResourceKey), ResponseCode.TransientFailure, ex)).ToArray();
            }
        }

        /// <inheritdoc/>
        public Task Release(string category, AcquiredLease[] acquiredLeases)
        {
            return this.leaseProvider.Release(category, acquiredLeases);
        }

        /// <inheritdoc/>
        public async Task<AcquireLeaseResult[]> Renew(string category, AcquiredLease[] acquiredLeases)
        {
            try
            {
                return await this.leaseProvider.Renew(category, acquiredLeases);
            }
            catch (Exception ex)
            {
                return acquiredLeases.Select(request => new AcquireLeaseResult(new AcquiredLease(request.ResourceKey), ResponseCode.TransientFailure, ex)).ToArray();
            }
        }

        private static IDevelopmentLeaseProviderGrain GetLeaseProviderGrain(IGrainFactory grainFactory)
        {
            return grainFactory.GetGrain<IDevelopmentLeaseProviderGrain>(0);
        }
    }

    internal interface IDevelopmentLeaseProviderGrain : ILeaseProvider, IGrainWithIntegerKey
    {
        /// <summary>
        /// 清除所有租约，用于模拟此 Grain 丢失或强制重新均衡队列。
        /// </summary>
        /// <returns></returns>
        [Alias("847FCE12")]
        Task Reset(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 在内存中存储租约信息的 Grain。
    /// TODO：考虑将其改为有状态 Grain，作为可与存储
    /// 提供程序配合使用且适用于生产环境的租约提供程序实现。
    /// </summary>
    internal class DevelopmentLeaseProviderGrain : Grain, IDevelopmentLeaseProviderGrain
    {
        private readonly Dictionary<Tuple<string, string>, Lease> leases = new Dictionary<Tuple<string, string>, Lease>();

        public Task<AcquireLeaseResult[]> Acquire(string category, LeaseRequest[] leaseRequests)
        {
            return Task.FromResult(leaseRequests.Select(request => Acquire(category, request)).ToArray());
        }

        public Task Release(string category, AcquiredLease[] acquiredLeases)
        {
            foreach (AcquiredLease lease in acquiredLeases)
            {
                Release(category, lease);
            }
            return Task.CompletedTask;
        }

        public Task<AcquireLeaseResult[]> Renew(string category, AcquiredLease[] acquiredLeases)
        {
            return Task.FromResult(acquiredLeases.Select(lease => Renew(category, lease)).ToArray());
        }

        public Task Reset(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.leases.Clear();
            return Task.CompletedTask;
        }

        private AcquireLeaseResult Acquire(string category, LeaseRequest leaseRequest)
        {
            DateTime now = DateTime.UtcNow;
            Lease lease = this.leases.GetValueOrAddNew(Tuple.Create(category, leaseRequest.ResourceKey));
            if (lease.ExpiredUtc < now)
            {
                lease.ExpiredUtc = now + leaseRequest.Duration;
                return new AcquireLeaseResult(new AcquiredLease(leaseRequest.ResourceKey, leaseRequest.Duration, lease.Token, now), ResponseCode.OK, null);
            }
            return new AcquireLeaseResult(new AcquiredLease(leaseRequest.ResourceKey), ResponseCode.LeaseNotAvailable, new OrleansException("Lease not available"));
        }

        private void Release(string category, AcquiredLease acquiredLease)
        {
            Tuple<string, string> leaseKey = Tuple.Create(category, acquiredLease.ResourceKey);
            if (this.leases.TryGetValue(leaseKey, out Lease? lease) && lease.Token == acquiredLease.Token)
            {
                leases.Remove(leaseKey);
            }
        }

        private AcquireLeaseResult Renew(string category, AcquiredLease acquiredLease)
        {
            DateTime now = DateTime.UtcNow;
            // 若租约存在、持有正确令牌且租约尚未过期，则续租。
            if (!this.leases.TryGetValue(Tuple.Create(category, acquiredLease.ResourceKey), out Lease? lease) || lease.Token != acquiredLease.Token)
            {
                return new AcquireLeaseResult(new AcquiredLease(acquiredLease.ResourceKey), ResponseCode.InvalidToken, new OrleansException("Invalid token provided, caller is not the owner."));
            }
            // 只要所有者未改变，就不关心租约是否已经过期。
            lease.ExpiredUtc = now + acquiredLease.Duration;
            return new AcquireLeaseResult(new AcquiredLease(acquiredLease.ResourceKey, acquiredLease.Duration, lease.Token, now), ResponseCode.OK, null);
        }

        private class Lease
        {
            private DateTime expiredUtc;

            public DateTime ExpiredUtc
            {
                get { return expiredUtc; }
                set
                {
                    expiredUtc = value;
                    Token = Guid.NewGuid().ToString();
                }
            }
            public string Token { get; private set; } = null!;
        }
    }
}

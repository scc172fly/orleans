using Orleans.LeaseProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Runtime.Development
{
    /// <summary>
    /// 用于开发和测试使用的记忆租赁供应商。
    /// 该供应商在存储租赁信息时将信息存储租赁,如果 grain
    /// 成为不活动或 silo 崩溃。执行仅是有意
    /// 用于试验或地方发展目的 -- -- 不生产用途。
    /// </summary>
    public class InMemoryLeaseProvider : ILeaseProvider
    {
        private readonly IDevelopmentLeaseProviderGrain leaseProvider;

        /// <summary>
        /// 初始化 <see cref="InMemoryLeaseProvider"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
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
        /// 忘记所有租赁。 用于模拟丢失此 grain 或强制重新平衡队列
        /// </summary>
        /// <returns></returns>
        [Alias("847FCE12")]
        Task Reset(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 存储信息以保存记忆的Grain 。
    /// 考虑让这个故事成为 grain将储存作为可行的生产方式,对与储存有关的租赁提供商实施
    /// 提供商。
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
            // 如果租约存在,我们有合适的标志 租赁期没有到期,续签
            if (!this.leases.TryGetValue(Tuple.Create(category, acquiredLease.ResourceKey), out Lease? lease) || lease.Token != acquiredLease.Token)
            {
                return new AcquireLeaseResult(new AcquiredLease(acquiredLease.ResourceKey), ResponseCode.InvalidToken, new OrleansException("Invalid token provided, caller is not the owner."));
            }
            // 我们不在乎租赁是否到期 只要业主没有改变
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

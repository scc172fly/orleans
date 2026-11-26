using System;
using System.Threading.Tasks;

namespace Orleans.LeaseProviders
{
    /// <summary>
    /// 后租赁
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class AcquiredLease
    {
        /// <summary>
        /// 租赁所附属的资源钥匙 
        /// </summary>
        [Id(0)]
        public string ResourceKey { get; }

        /// <summary>
        /// 租期
        /// </summary>
        [Id(1)]
        public TimeSpan Duration { get; }

        /// <summary>
        /// 租赁凭证,如果租约的获得或续续租失败,则无效
        /// </summary>
        [Id(2)]
        public string? Token { get; }

        /// <summary>
        /// 本次租赁的起算时间,即租约获得或续租时
        /// </summary>
        [Id(3)]
        public DateTime StartTimeUtc { get; }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="resourceKey"></param>
        /// <param name="duration"></param>
        /// <param name="token"></param>
        /// <param name="startTimeUtc"></param>
        public AcquiredLease(string resourceKey, TimeSpan duration, string? token, DateTime startTimeUtc)
        {
            this.ResourceKey = resourceKey;
            this.Duration = duration;
            this.Token = token;
            this.StartTimeUtc = startTimeUtc;
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="resourceKey"></param>
        public AcquiredLease(string resourceKey)
        {
            this.ResourceKey = resourceKey;
        }
    }

    /// <summary>
    /// AcquireLeaseResult 类,证明获得或续展租赁经营的结果
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class AcquireLeaseResult
    {
        /// <summary>
        /// 获得租赁,如果购置或续展业务失败,则无效。
        /// </summary>
        [Id(0)]
        public AcquiredLease? AcquiredLease { get; }

        /// <summary>
        /// 答复状态
        /// </summary>
        [Id(1)]
        public ResponseCode StatusCode { get; }

        /// <summary>
        /// 如果获得或续租失败,则造成这一例外。如果操作成功,此字段将无效。
        /// </summary>
        [Id(2)]
        public Exception? FailureException { get; }

        /// <summary>
        /// 初始化 <see cref="AcquireLeaseResult"/> 类的新实例。
        /// </summary>
        /// <param name="acquiredLease">获得或续租,或 <see langword="null"/> 如果操作失败。</param>
        /// <param name="statusCode">作战状态。</param>
        /// <param name="failureException">导致操作失败的例外,如果有的话。</param>
        public AcquireLeaseResult(AcquiredLease? acquiredLease, ResponseCode statusCode, Exception? failureException)
        {
            this.AcquiredLease = acquiredLease;
            this.StatusCode = statusCode;
            this.FailureException = failureException;
        }
    }

    /// <summary>
    /// 说明租赁购置或续租业务的结果。
    /// </summary>
    [GenerateSerializer]
    public enum ResponseCode
    {
        /// <summary>
        /// 操作成功
        /// </summary>
        OK,
        /// <summary>
        /// 租赁由其他实体拥有
        /// </summary>
        LeaseNotAvailable,
        /// <summary>
        /// AcquiredLease 中的质标无效,这意味着租约到期
        /// </summary>
        InvalidToken,
        /// <summary>
        /// TransientFailure,应该可以检索。 
        /// </summary>
        TransientFailure
    }

    /// <summary>
    /// 租赁请求,请注明 ResourceKey 和租约期限。 
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class LeaseRequest
    {
        /// <summary>
        /// 您想要应用租赁的资源的密钥
        /// </summary>
        [Id(0)]
        public string ResourceKey { get; }

        /// <summary>
        /// 租约期限
        /// </summary>
        [Id(1)]
        public TimeSpan Duration { get; }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="resourceKey"></param>
        /// <param name="duration"></param>
        public LeaseRequest(string resourceKey, TimeSpan duration)
        {
            this.ResourceKey = resourceKey;
            this.Duration = duration;
        }
    }

    /// <summary>
    /// 租赁供应商接口 
    /// </summary>
    public interface ILeaseProvider
    {
        /// <summary>
        /// 批次购货租赁经营
        /// </summary>
        /// <param name="category">资源类别</param>
        /// <param name="leaseRequests"></param>
        /// <returns>租赁获取结果阵列,其顺序与 leaseRequstes</returns>相同
        Task<AcquireLeaseResult[]> Acquire(string category, LeaseRequest[] leaseRequests);
        /// <summary>
        /// 批次续期租赁业务
        /// </summary>
        /// <param name="category">资源类别</param>
        /// <param name="aquiredLeases"></param>
        /// <returns>租赁续期结果阵列,其顺序与 acquiredLeases</returns>相同
        Task<AcquireLeaseResult[]> Renew(string category, AcquiredLease[] aquiredLeases);
        /// <summary>
        /// 批次放货租赁租赁业务
        /// </summary>
        /// <param name="category">资源类别</param>
        /// <param name="aquiredLeases"></param>
        /// <returns></returns>
        Task Release(string category, AcquiredLease[] aquiredLeases);
    }
}

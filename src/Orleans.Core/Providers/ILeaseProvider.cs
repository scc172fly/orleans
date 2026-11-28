using System;
using System.Threading.Tasks;

namespace Orleans.LeaseProviders
{
    /// <summary>
    /// 已获取的租约。
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class AcquiredLease
    {
        /// <summary>
        /// 租约关联的资源键。
        /// </summary>
        [Id(0)]
        public string ResourceKey { get; }

        /// <summary>
        /// 所获取租约的有效时长。
        /// </summary>
        [Id(1)]
        public TimeSpan Duration { get; }

        /// <summary>
        /// 租约令牌；如果获取或续订租约失败，则为 null。
        /// </summary>
        [Id(2)]
        public string? Token { get; }

        /// <summary>
        /// 调用方记录的租约开始时间，即获取或续订租约时的时间。
        /// </summary>
        [Id(3)]
        public DateTime StartTimeUtc { get; }

        /// <summary>
        /// 构造函数。
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
        /// 构造函数。
        /// </summary>
        /// <param name="resourceKey"></param>
        public AcquiredLease(string resourceKey)
        {
            this.ResourceKey = resourceKey;
        }
    }

    /// <summary>
    /// AcquireLeaseResult 类，表示获取或续订租约操作的结果。
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class AcquireLeaseResult
    {
        /// <summary>
        /// 已获取的租约；如果获取或续订操作失败，则为 null。
        /// </summary>
        [Id(0)]
        public AcquiredLease? AcquiredLease { get; }

        /// <summary>
        /// 响应状态。
        /// </summary>
        [Id(1)]
        public ResponseCode StatusCode { get; }

        /// <summary>
        /// 如果获取或续订租约失败，则为导致失败的异常。操作成功时，此字段为 null。
        /// </summary>
        [Id(2)]
        public Exception? FailureException { get; }

        /// <summary>
        /// 初始化 <see cref="AcquireLeaseResult"/> 类的新实例。
        /// </summary>
        /// <param name="acquiredLease">已获取或续订的租约；如果操作失败，则为 <see langword="null"/>。</param>
        /// <param name="statusCode">操作状态。</param>
        /// <param name="failureException">导致操作失败的异常（如果存在）。</param>
        public AcquireLeaseResult(AcquiredLease? acquiredLease, ResponseCode statusCode, Exception? failureException)
        {
            this.AcquiredLease = acquiredLease;
            this.StatusCode = statusCode;
            this.FailureException = failureException;
        }
    }

    /// <summary>
    /// 标识租约获取或续订操作的结果。
    /// </summary>
    [GenerateSerializer]
    public enum ResponseCode
    {
        /// <summary>
        /// 操作成功。
        /// </summary>
        OK,
        /// <summary>
        /// 租约由其他实体持有。
        /// </summary>
        LeaseNotAvailable,
        /// <summary>
        /// AcquiredLease 中的令牌无效，意味着租约已过期。
        /// </summary>
        InvalidToken,
        /// <summary>
        /// TransientFailure（暂时性故障），应允许重试。
        /// </summary>
        TransientFailure
    }

    /// <summary>
    /// 租约请求，可在其中指定 ResourceKey 和租约有效时长。
    /// </summary>
    [GenerateSerializer, Immutable]
    public sealed class LeaseRequest
    {
        /// <summary>
        /// 要为其申请租约的资源键。
        /// </summary>
        [Id(0)]
        public string ResourceKey { get; }

        /// <summary>
        /// 租约的有效时长。
        /// </summary>
        [Id(1)]
        public TimeSpan Duration { get; }

        /// <summary>
        /// 构造函数。
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
    /// 租约提供程序接口。
    /// </summary>
    public interface ILeaseProvider
    {
        /// <summary>
        /// 批量获取租约操作。
        /// </summary>
        /// <param name="category">资源类别。</param>
        /// <param name="leaseRequests"></param>
        /// <returns>租约获取结果数组，其顺序与 leaseRequests 一致。</returns>
        Task<AcquireLeaseResult[]> Acquire(string category, LeaseRequest[] leaseRequests);
        /// <summary>
        /// 批量续订租约操作。
        /// </summary>
        /// <param name="category">资源类别。</param>
        /// <param name="aquiredLeases"></param>
        /// <returns>租约续订结果数组，其顺序与 acquiredLeases 一致。</returns>
        Task<AcquireLeaseResult[]> Renew(string category, AcquiredLease[] aquiredLeases);
        /// <summary>
        /// 批量释放租约操作。
        /// </summary>
        /// <param name="category">资源类别。</param>
        /// <param name="aquiredLeases"></param>
        /// <returns></returns>
        Task Release(string category, AcquiredLease[] aquiredLeases);
    }
}

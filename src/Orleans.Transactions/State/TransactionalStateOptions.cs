using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置事务状态的事务处理机制。
    /// </summary>
    public class TransactionalStateOptions
    {
        /// <summary>
        /// 获取或设置事务组持有状态锁的基础时长。实际时长至少等于事务超时时间。
        /// </summary>
        public TimeSpan LockTimeout { get; set; } = DefaultLockTimeout;

        /// <summary>
        /// <see cref="LockTimeout"/> 的默认值。
        /// </summary>
        public static TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(8);

        /// <summary>
        /// 获取或设置事务管理器等待准备阶段完成的最长时长。
        /// </summary>
        public TimeSpan PrepareTimeout { get; set; } = DefaultPrepareTimeout;

        /// <summary>
        /// 获取 <see cref="PrepareTimeout"/> 的默认值。
        /// </summary>
        public static TimeSpan DefaultPrepareTimeout => TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设置事务等待获取状态锁的最长时长。
        /// </summary>
        public TimeSpan LockAcquireTimeout { get; set; } = DefaultLockAcquireTimeout;

        /// <summary>
        /// 获取 <see cref="LockAcquireTimeout"/> 的默认值。
        /// </summary>
        public static TimeSpan DefaultLockAcquireTimeout => TimeSpan.FromSeconds(10);

        /// <summary>
        /// 获取或设置针对远程事务发送存活通知的时间间隔。
        /// </summary>
        public TimeSpan RemoteTransactionPingFrequency { get; set; } = DefaultRemoteTransactionPingFrequency;

        /// <summary>
        /// <see cref="RemoteTransactionPingFrequency"/> 的默认值。
        /// </summary>
        public static TimeSpan DefaultRemoteTransactionPingFrequency = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 获取或设置尝试确认已提交事务的重试间隔。
        /// </summary>
        public TimeSpan ConfirmationRetryDelay { get; set; } = DefaultConfirmationRetryDelay;
        private static TimeSpan DefaultConfirmationRetryDelay => TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置为配置兼容性而保留的旧版确认重试次数限制。
        /// </summary>
        /// <remarks>运行时会重试确认，直至确认成功或激活实例被取消。</remarks>
        public static int ConfirmationRetryLimit { get; set; } = DefaultConfirmationRetryLimit;

        /// <summary>
        /// <see cref="ConfirmationRetryLimit"/> 的默认值。
        /// </summary>
        public const int DefaultConfirmationRetryLimit = 3;

        /// <summary>
        /// 获取或设置可共享一个锁组的最大事务数量。
        /// </summary>
        public int MaxLockGroupSize { get; set; } = DefaultMaxLockGroupSize;

        /// <summary>
        /// <see cref="MaxLockGroupSize"/> 的默认值。
        /// </summary>
        public const int DefaultMaxLockGroupSize = 20;

    }
}

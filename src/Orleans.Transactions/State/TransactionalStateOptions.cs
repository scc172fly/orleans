using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置交易处理程序,以适应交易状态。
    /// </summary>
    public class TransactionalStateOptions
    {
        /// <summary>
        /// 获取或设定交易组保留状态锁定的基准期限。 有效期限至少是交易超时 。
        /// </summary>
        public TimeSpan LockTimeout { get; set; } = DefaultLockTimeout;

        /// <summary>
        /// 默认值 <see cref="LockTimeout"/>。
        /// </summary>
        public static TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(8);

        /// <summary>
        /// 设置交易经理等待准备阶段完成的最长期限。
        /// </summary>
        public TimeSpan PrepareTimeout { get; set; } = DefaultPrepareTimeout;

        /// <summary>
        /// 获取 <see cref="PrepareTimeout"/>的默认值。
        /// </summary>
        public static TimeSpan DefaultPrepareTimeout => TimeSpan.FromSeconds(20);

        /// <summary>
        /// 获取或设定交易等待获得国家锁的最长期限。
        /// </summary>
        public TimeSpan LockAcquireTimeout { get; set; } = DefaultLockAcquireTimeout;

        /// <summary>
        /// 获取 <see cref="LockAcquireTimeout"/>的默认值。
        /// </summary>
        public static TimeSpan DefaultLockAcquireTimeout => TimeSpan.FromSeconds(10);

        /// <summary>
        /// 获取或设定发送到远程交易的活性通知之间的间隔。
        /// </summary>
        public TimeSpan RemoteTransactionPingFrequency { get; set; } = DefaultRemoteTransactionPingFrequency;

        /// <summary>
        /// 默认值 <see cref="RemoteTransactionPingFrequency"/>。
        /// </summary>
        public static TimeSpan DefaultRemoteTransactionPingFrequency = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 获取或设定在试图确认已实施交易之间的延迟。
        /// </summary>
        public TimeSpan ConfirmationRetryDelay { get; set; } = DefaultConfirmationRetryDelay;
        private static TimeSpan DefaultConfirmationRetryDelay => TimeSpan.FromSeconds(30);

        /// <summary>
        /// 获取或设置为配置兼容而保留的遗留确认重试限制。
        /// </summary>
        /// <remarks>运行时重试确认,直到它成功或激活被取消。</remarks>
        public static int ConfirmationRetryLimit { get; set; } = DefaultConfirmationRetryLimit;

        /// <summary>
        /// 默认值 <see cref="ConfirmationRetryLimit"/>。
        /// </summary>
        public const int DefaultConfirmationRetryLimit = 3;

        /// <summary>
        /// 获取或设定可以共享锁定组的最大交易数量。
        /// </summary>
        public int MaxLockGroupSize { get; set; } = DefaultMaxLockGroupSize;

        /// <summary>
        /// 默认值 <see cref="MaxLockGroupSize"/>。
        /// </summary>
        public const int DefaultMaxLockGroupSize = 20;

    }
}

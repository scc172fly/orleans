namespace Orleans.Providers
{
    /// <summary>
    /// 提供者使用的常数。
    /// </summary>
    public static class ProviderConstants
    {
        /// <summary>
        /// 默认的存储提供者名 。
        /// </summary>
        public const string DEFAULT_STORAGE_PROVIDER_NAME = "Default";

        /// <summary>
        /// 默认日志一致性提供者名称 。
        /// </summary>
        public const string DEFAULT_LOG_CONSISTENCY_PROVIDER_NAME = "Default";

        /// <summary>
        /// 串流阴道/子使用的默认 grain 存储提供者名称 。
        /// </summary>
        public const string DEFAULT_PUBSUB_PROVIDER_NAME = "PubSubStore";
    }
}

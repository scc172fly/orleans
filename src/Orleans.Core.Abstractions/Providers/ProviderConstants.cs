namespace Orleans.Providers
{
    /// <summary>
    /// 提供程序使用的常量值。
    /// </summary>
    public static class ProviderConstants
    {
        /// <summary>
        /// 默认存储提供程序名称。
        /// </summary>
        public const string DEFAULT_STORAGE_PROVIDER_NAME = "Default";

        /// <summary>
        /// 默认日志一致性提供程序名称。
        /// </summary>
        public const string DEFAULT_LOG_CONSISTENCY_PROVIDER_NAME = "Default";

        /// <summary>
        /// 流式发布/订阅使用的默认 Grain 存储提供程序名称。
        /// </summary>
        public const string DEFAULT_PUBSUB_PROVIDER_NAME = "PubSubStore";
    }
}

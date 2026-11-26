namespace Orleans
{
    /// <summary>
    /// 描述客户端和 silo 初始化期间调用的一个配置验证符。
    /// </summary>
    public interface IConfigurationValidator
    {
        /// <summary>
        /// 校验系统配置, 如果配置无效则丢弃例外 。
        /// </summary>
        void ValidateConfiguration();
    }
}

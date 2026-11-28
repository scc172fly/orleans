namespace Orleans
{
    /// <summary>
    /// 描述在客户端和 Silo 初始化期间调用的配置验证器。
    /// </summary>
    public interface IConfigurationValidator
    {
        /// <summary>
        /// 验证系统配置；如果配置无效，则抛出异常。
        /// </summary>
        void ValidateConfiguration();
    }
}

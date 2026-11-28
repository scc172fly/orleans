namespace Orleans.Runtime
{
    internal enum ActivationState
    {
        /// <summary>
        /// 正在创建激活实例。
        /// </summary>
        Creating,

        /// <summary>
        /// 激活实例正在激活过程中。
        /// </summary>
        Activating,

        /// <summary>
        /// 激活实例已成功激活，准备处理请求。
        /// </summary>
        Valid,

        /// <summary>
        /// 激活实例正在停用过程中。
        /// </summary>
        Deactivating,

        /// <summary>
        /// 已终止激活实例的墓碑状态。
        /// </summary>
        Invalid
    }
}

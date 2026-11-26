namespace Orleans.Runtime
{
    internal enum ActivationState
    {
        /// <summary>
        /// 正在创建激活
        /// </summary>
        Creating,

        /// <summary>
        /// 启动进程正在进行之中。
        /// </summary>
        Activating,

        /// <summary>
        /// 启动工作成功启动,并准备处理请求。
        /// </summary>
        Valid,

        /// <summary>
        /// 启动处于停用过程的中间。
        /// </summary>
        Deactivating,

        /// <summary>
        /// 引爆的墓碑已经终止
        /// </summary>
        Invalid
    }
}

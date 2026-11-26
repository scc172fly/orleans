namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 表示应请求作出回应的可兑现承诺。
    /// </summary>
    public interface IResponseCompletionSource
    {
        /// <summary>
        /// 设置结果 。
        /// </summary>
        /// <param name="value">结果值。</param>
        void Complete(Response value);

        /// <summary>
        /// 将结果设为默认值。
        /// </summary>
        void Complete();
    }
}

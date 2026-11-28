namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 表示可完成的请求响应承诺对象（Promise）。
    /// </summary>
    public interface IResponseCompletionSource
    {
        /// <summary>
        /// 设置结果。
        /// </summary>
        /// <param name="value">结果值。</param>
        void Complete(Response value);

        /// <summary>
        /// 将结果设置为默认值。
        /// </summary>
        void Complete();
    }
}

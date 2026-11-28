namespace Orleans.Streams
{
    /// <summary>
    /// 隐式订阅流的 Grain 的扩展方法。
    /// </summary>
    public static class ImplicitConsumerGrainExtensions
    {
        /// <summary>
        /// 构造 Grain 隐式订阅的流的 <see cref="StreamIdentity"/>。
        /// </summary>
        /// <param name="grain">隐式订阅该流的 Grain。</param>
        /// <returns>流标识（键与命名空间）。</returns>
        public static StreamIdentity GetImplicitStreamIdentity(this IGrainWithGuidCompoundKey grain)
        {
            string? keyExtension;
            var key = grain.GetPrimaryKey(out keyExtension);
            return new StreamIdentity(key, keyExtension);
        }
    }
}

namespace Orleans.Streams
{
    /// <summary>
    /// 默认订阅流的 grains 扩展方法。
    /// </summary>
    public static class ImplicitConsumerGrainExtensions
    {
        /// <summary>
        /// 构造构造 <see cref="StreamIdentity"/> 向导 grain 默示同意。
        /// </summary>
        /// <param name="grain">默示接受者 grain.</param>
        /// <returns>流源身份(密钥+命名空间)</returns>
        public static StreamIdentity GetImplicitStreamIdentity(this IGrainWithGuidCompoundKey grain)
        {
            string? keyExtension;
            var key = grain.GetPrimaryKey(out keyExtension);
            return new StreamIdentity(key, keyExtension);
        }
    }
}

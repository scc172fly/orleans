namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// <see cref="ResponseCompletionSource"/> 和 <see cref="ResponseCompletionSource{TResult}"/> 的对象池。
    /// </summary>
    public static class ResponseCompletionSourcePool
    {
        internal static readonly ConcurrentObjectPool<ResponseCompletionSource, DefaultConcurrentObjectPoolPolicy<ResponseCompletionSource>> UntypedPool = new(new());

        /// <summary>
        /// 从池中获取值。
        /// </summary>
        /// <typeparam name="T">底层结果类型。</typeparam>
        /// <returns>从池中获取的值。</returns>
        public static ResponseCompletionSource<T> Get<T>() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 将值归还池中。
        /// </summary>
        /// <typeparam name="T">底层结果类型。</typeparam>
        /// <param name="obj">要归还到池中的值。</param>
        public static void Return<T>(ResponseCompletionSource<T> obj) => TypedPool<T>.Pool.Return(obj);

        /// <summary>
        /// 从池中获取值。
        /// </summary>
        /// <returns>从池中获取的值。</returns>
        public static ResponseCompletionSource Get() => UntypedPool.Get();

        /// <summary>
        /// 将值归还池中。
        /// </summary>
        /// <param name="obj">要归还到池中的值。</param>
        public static void Return(ResponseCompletionSource obj) => UntypedPool.Return(obj);

        private static class TypedPool<T>
        {
            public static readonly ConcurrentObjectPool<ResponseCompletionSource<T>, DefaultConcurrentObjectPoolPolicy<ResponseCompletionSource<T>>> Pool = new(new());
        }
    }
}

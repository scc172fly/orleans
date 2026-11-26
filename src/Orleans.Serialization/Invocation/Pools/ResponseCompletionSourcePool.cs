namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// <see cref="ResponseCompletionSource"/> 和 <see cref="ResponseCompletionSource{TResult}"/>的对象集合。
    /// </summary>
    public static class ResponseCompletionSourcePool
    {
        internal static readonly ConcurrentObjectPool<ResponseCompletionSource, DefaultConcurrentObjectPoolPolicy<ResponseCompletionSource>> UntypedPool = new(new());

        /// <summary>
        /// 从池子里取出一个价值。
        /// </summary>
        /// <typeparam name="T">基本结果类型。</typeparam>
        /// <returns>集合中的数值。</returns>
        public static ResponseCompletionSource<T> Get<T>() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 返回池内值。
        /// </summary>
        /// <typeparam name="T">基本结果类型。</typeparam>
        /// <param name="obj">返回集合的值</param>
        public static void Return<T>(ResponseCompletionSource<T> obj) => TypedPool<T>.Pool.Return(obj);

        /// <summary>
        /// 从池子里取出一个价值。
        /// </summary>
        /// <returns>集合中的数值。</returns>
        public static ResponseCompletionSource Get() => UntypedPool.Get();

        /// <summary>
        /// 返回池内值。
        /// </summary>
        /// <param name="obj">返回集合的值</param>
        public static void Return(ResponseCompletionSource obj) => UntypedPool.Return(obj);

        private static class TypedPool<T>
        {
            public static readonly ConcurrentObjectPool<ResponseCompletionSource<T>, DefaultConcurrentObjectPoolPolicy<ResponseCompletionSource<T>>> Pool = new(new());
        }
    }
}

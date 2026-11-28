namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// <see cref="Response{TResult}"/> 值的对象池。
    /// </summary>
    public static class ResponsePool
    {
        /// <summary>
        /// 从池中获取值。
        /// </summary>
        /// <typeparam name="T">底层响应类型。</typeparam>
        /// <returns>从池中获取的值。</returns>
        public static Response<T> Get<T>() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 将值归还池中。
        /// </summary>
        /// <typeparam name="T">底层响应类型。</typeparam>
        /// <param name="obj">要归还到池中的值。</param>
        public static void Return<T>(Response<T> obj) => TypedPool<T>.Pool.Return(obj);

        private static class TypedPool<T>
        {
            public static readonly ConcurrentObjectPool<Response<T>> Pool = new();
        }
    }
}

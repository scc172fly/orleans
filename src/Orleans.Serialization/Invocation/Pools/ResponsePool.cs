namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 对象集合,以 <see cref="Response{TResult}"/> 值计算。
    /// </summary>
    public static class ResponsePool
    {
        /// <summary>
        /// 从池子里取出一个价值。
        /// </summary>
        /// <typeparam name="T">基本反应类型。</typeparam>
        /// <returns>集合中的数值。</returns>
        public static Response<T> Get<T>() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 返回池内值。
        /// </summary>
        /// <typeparam name="T">基本反应类型。</typeparam>
        /// <param name="obj">返回集合值。</param>
        public static void Return<T>(Response<T> obj) => TypedPool<T>.Pool.Return(obj);

        private static class TypedPool<T>
        {
            public static readonly ConcurrentObjectPool<Response<T>> Pool = new();
        }
    }
}

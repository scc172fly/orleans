namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// <see cref="IInvokable"/> 执行对象集合。
    /// </summary>
    public static class InvokablePool
    {
        /// <summary>
        /// 从池子里取出一个价值。
        /// </summary>
        /// <typeparam name="T">数值的类型。</typeparam>
        /// <returns>集合中的数值。</returns>
        public static T Get<T>() where T : class, IInvokable, new() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 返回池内值。
        /// </summary>
        /// <typeparam name="T">数值的类型。</typeparam>
        /// <param name="obj">返回值 。</param>
        public static void Return<T>(T obj) where T : class, IInvokable, new() => TypedPool<T>.Pool.Return(obj);

        private static class TypedPool<T> where T : class, IInvokable, new()
        {
            public static readonly ConcurrentObjectPool<T> Pool = new();
        }
    }
}

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// <see cref="IInvokable"/> 实现的对象池。
    /// </summary>
    public static class InvokablePool
    {
        /// <summary>
        /// 从池中获取值。
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <returns>从池中获取的值。</returns>
        public static T Get<T>() where T : class, IInvokable, new() => TypedPool<T>.Pool.Get();

        /// <summary>
        /// 将值归还池中。
        /// </summary>
        /// <typeparam name="T">值的类型。</typeparam>
        /// <param name="obj">要归还的值。</param>
        public static void Return<T>(T obj) where T : class, IInvokable, new() => TypedPool<T>.Pool.Return(obj);

        private static class TypedPool<T> where T : class, IInvokable, new()
        {
            public static readonly ConcurrentObjectPool<T> Pool = new();
        }
    }
}

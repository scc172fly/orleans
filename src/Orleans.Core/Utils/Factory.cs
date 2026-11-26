namespace Orleans
{
    /// <summary>
    /// 创建 <typeparamref name="TInstance"/>实例。
    /// </summary>
    /// <typeparam name="TInstance"></typeparam>
    /// <returns>实例。</returns>
    public delegate TInstance Factory<out TInstance>();

    /// <summary>
    /// 创建 <typeparamref name="TInstance"/>实例。
    /// </summary>
    /// <typeparam name="TInstance">实例类型。</typeparam>
    /// <typeparam name="TParam1">参数类型。</typeparam>
    /// <returns>实例。</returns>
    public delegate TInstance Factory<in TParam1, out TInstance>(TParam1 param1);

    /// <summary>
    /// 创建 <typeparamref name="TInstance"/>实例。
    /// </summary>
    /// <typeparam name="TInstance">实例类型。</typeparam>
    /// <typeparam name="TParam1">第一个参数类型。</typeparam>
    /// <typeparam name="TParam2">第二个参数类型。</typeparam>
    /// <returns>实例。</returns>
    public delegate TInstance Factory<in TParam1, in TParam2, out TInstance>(TParam1 param1, TParam2 param2);

    /// <summary>
    /// 创建 <typeparamref name="TInstance"/>实例。
    /// </summary>
    /// <typeparam name="TInstance">实例类型。</typeparam>
    /// <typeparam name="TParam1">第一个参数类型。</typeparam>
    /// <typeparam name="TParam2">第二个参数类型。</typeparam>
    /// <typeparam name="TParam3">第三参数类型 。</typeparam>
    /// <returns>实例。</returns>
    public delegate TInstance Factory<in TParam1, in TParam2, in TParam3, out TInstance>(TParam1 param1, TParam2 param2, TParam3 param3);
}

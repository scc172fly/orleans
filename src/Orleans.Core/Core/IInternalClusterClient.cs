namespace Orleans
{
    /// <summary>
    /// 面向内部使用的客户端接口。
    /// </summary>
    internal interface IInternalClusterClient : IClusterClient, IInternalGrainFactory
    {
    }
}

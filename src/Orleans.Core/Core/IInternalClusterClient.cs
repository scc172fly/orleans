namespace Orleans
{
    /// <summary>
    /// 内部定位客户界面 。
    /// </summary>
    internal interface IInternalClusterClient : IClusterClient, IInternalGrainFactory
    {
    }
}

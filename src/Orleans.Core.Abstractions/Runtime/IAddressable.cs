namespace Orleans.Runtime
{
    /// <summary>
    /// 可寻址端点的标记接口，例如 Grain、观察者以及其他系统内部的可寻址端点。
    /// </summary>
    [GenerateMethodSerializers(typeof(GrainReference))]
    public interface IAddressable
    {
    }
}

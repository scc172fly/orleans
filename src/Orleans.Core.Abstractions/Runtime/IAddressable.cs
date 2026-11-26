namespace Orleans.Runtime
{
    /// <summary>
    /// 可标定端点的标记界面, 如 grains、观察者和其他系统内部可标定的端点
    /// </summary>
    [GenerateMethodSerializers(typeof(GrainReference))]
    public interface IAddressable
    {
    }
}

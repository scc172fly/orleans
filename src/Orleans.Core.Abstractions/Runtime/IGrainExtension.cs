namespace Orleans.Runtime
{
    /// <summary>
    /// Grain 扩展的标记接口，由内部运行时扩展端点使用。
    /// </summary>
    [GenerateMethodSerializers(typeof(GrainReference), isExtension: true)]
    public interface IGrainExtension : IAddressable
    {
    }
}

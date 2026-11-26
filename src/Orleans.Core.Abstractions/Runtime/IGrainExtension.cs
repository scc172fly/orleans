namespace Orleans.Runtime
{
    /// <summary>
    /// grain 扩展的标记界面,用于内部运行时间扩展端点。
    /// </summary>
    [GenerateMethodSerializers(typeof(GrainReference), isExtension: true)]
    public interface IGrainExtension : IAddressable
    {
    }
}

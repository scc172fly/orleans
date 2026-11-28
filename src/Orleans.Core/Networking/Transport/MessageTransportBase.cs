namespace Orleans.Connections.Transport;

/// <summary>
/// <see cref="MessageTransport"/> 实现的基类。
/// </summary>
public abstract class MessageTransportBase : MessageTransport
{
    /// <summary>
    /// 获取此传输层的功能。
    /// </summary>
    public override FeatureCollection Features { get; } = new FeatureCollection();
}

namespace Orleans.Connections.Transport;

/// <summary>
/// <see cref="MessageTransport"/> 执行的基准等级。
/// </summary>
public abstract class MessageTransportBase : MessageTransport
{
    /// <summary>
    /// 获得运输的特征。
    /// </summary>
    public override FeatureCollection Features { get; } = new FeatureCollection();
}

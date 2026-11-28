internal readonly record struct ClientContext(
    IClusterClient Client,
    string? UserName = null,
    string? CurrentChannel = null)
{
    /// <summary>
    /// 检查客户端是否已连接到频道。
    /// </summary>
    public bool IsConnectedToChannel => CurrentChannel is not null;
}

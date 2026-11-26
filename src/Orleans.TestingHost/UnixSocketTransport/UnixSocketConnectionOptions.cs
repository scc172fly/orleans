using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace Orleans.TestingHost.UnixSocketTransport;

/// <summary>
/// Orleans Unix 域套接字传输选项。
/// </summary>
public partial class UnixSocketConnectionOptions
{
    /// <summary>
    /// 获取或设置函数,以绘制 Unix 域套接字路径的终点。
    /// </summary>
    public Func<EndPoint, string> ConvertEndpointToPath { get; set; } = DefaultConvertEndpointToPath;

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex ConvertEndpointRegex();

    private static string DefaultConvertEndpointToPath(EndPoint endPoint) => Path.Combine(Path.GetTempPath(), ConvertEndpointRegex().Replace(endPoint.ToString()!, "_"));
}

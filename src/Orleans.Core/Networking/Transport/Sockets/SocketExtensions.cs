using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Orleans.Connections.Transport.Sockets;

internal static class SocketExtensions
{
    private const int SIO_LOOPBACK_FAST_PATH = -1744830448;
    private static readonly byte[] Enabled = BitConverter.GetBytes(1);

    internal static void ConfigureKeepAlive(this Socket socket, TcpMessageTransportOptions options, ILogger logger) =>
        ConfigureKeepAlive(options, logger, socket.SetSocketOption);

    internal static void ConfigureKeepAlive(
        TcpMessageTransportOptions options,
        ILogger logger,
        Action<SocketOptionLevel, SocketOptionName, int> setSocketOption)
    {
        if (!TrySet(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, options.KeepAlive ? 1 : 0) || !options.KeepAlive)
        {
            return;
        }

        TrySet(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, options.KeepAliveTimeSeconds);
        TrySet(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, options.KeepAliveIntervalSeconds);
        TrySet(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, options.KeepAliveRetryCount);

        bool TrySet(SocketOptionLevel level, SocketOptionName option, int value)
        {
            try
            {
                setSocketOption(level, option, value);
                return true;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is
                SocketError.ProtocolOption or SocketError.OperationNotSupported or
                SocketError.ProtocolNotSupported or SocketError.SocketNotSupported)
            {
                SocketsLog.UnsupportedKeepAliveOption(logger, exception, option);
                return false;
            }
            catch (PlatformNotSupportedException exception)
            {
                SocketsLog.UnsupportedKeepAliveOption(logger, exception, option);
                return false;
            }
        }
    }

    /// <summary>
    /// 启用 TCP 圈背快速路径。
    /// 见 https://blogs.technet.microsoft.com/wincat/2012/12/05/fast-tcp-loopback-performance-and-low-latency-with-windows-server-2012-tcp-loopback-fast-path/
    /// 以供参考。
    /// </summary>
    /// <param name="socket">启用的套接字 FastPath 。</param>
    public static void EnableFastPath(this Socket socket, bool noDelay = true)
    {
        if (noDelay)
        {
            try { socket.NoDelay = true; } catch { }
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // 仅 Win8/ Server2012+
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major > 6 || osVersion.Major == 6 && osVersion.Minor >= 2)
            {
                socket.IOControl(SIO_LOOPBACK_FAST_PATH, Enabled, null);
            }
        }
        catch
        {
            // 如果机器上的操作系统版本
            // 支持 SIO_LOOPBACK_ FAST_ PATH (i.e. 版本) 不支持
            // 在Windows 8/ Windows服务器(2012)之前处理例外
        }
    }
}

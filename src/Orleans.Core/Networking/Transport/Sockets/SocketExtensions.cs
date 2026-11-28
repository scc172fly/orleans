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
    /// 在套接字上启用 TCP Loopback Fast Path。
    /// 参阅 https://blogs.technet.microsoft.com/wincat/2012/12/05/fast-tcp-loopback-performance-and-low-latency-with-windows-server-2012-tcp-loopback-fast-path/
    /// 了解更多信息。
    /// </summary>
    /// <param name="socket">应启用 FastPath 的套接字。</param>
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
            // 仅适用于 Windows 8 / Windows Server 2012 及更高版本。
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major > 6 || osVersion.Major == 6 && osVersion.Minor >= 2)
            {
                socket.IOControl(SIO_LOOPBACK_FAST_PATH, Enabled, null);
            }
        }
        catch
        {
            // 如果此计算机的操作系统版本
            // 不支持 SIO_LOOPBACK_FAST_PATH（即版本
            // 低于 Windows 8 / Windows Server 2012），则处理该异常。
        }
    }
}

// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport.Sockets;

internal sealed class SocketReceiver : SocketAwaitableEventArgs
{
    public SocketReceiver()
    {
    }

    public ValueTask ReceiveAsync(Socket socket, List<ArraySegment<byte>> buffers)
    {
        BufferList = buffers;

        if (socket.ReceiveAsync(this))
        {
            return new ValueTask(this, 0);
        }

        var error = Error;
        return error is not null ? ValueTask.FromException(error) : default;
    }
}

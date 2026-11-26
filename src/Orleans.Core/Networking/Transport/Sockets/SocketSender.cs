// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Orleans.Connections.Transport.Sockets;

internal sealed class SocketSender : SocketAwaitableEventArgs
{
    private List<ArraySegment<byte>>? _bufferList;

    public SocketSender()
    {
    }

    public ValueTask SendAsync(Socket socket, in ReadOnlySequence<byte> buffers)
    {
        if (buffers.IsSingleSegment)
        {
            return SendAsync(socket, buffers.First);
        }

        SetBufferList(buffers);

        if (socket.SendAsync(this))
        {
            return new ValueTask(this, 0);
        }

        var error = Error;
        return error is not null ? ValueTask.FromException(error) : default;
    }

    public ValueTask SendAsync(Socket socket, List<ArraySegment<byte>> buffers)
    {
        if (!MemoryBuffer.IsEmpty)
        {
            SetBuffer(null, 0, 0);
        }

        BufferList = buffers;

        if (socket.SendAsync(this))
        {
            return new ValueTask(this, 0);
        }

        var error = Error;
        return error is not null ? ValueTask.FromException(error) : default;
    }

    public void Reset()
    {
        // 在我们把它放回池子之前,我们先清清缓冲和缓冲清单
        // 但它能消除混乱 当它看着垃圾场看这些垃圾堆时
        // 缓冲器回到池子里时,它会紧紧抓住缓冲剂
        if (BufferList != null)
        {
            BufferList = null;

            _bufferList?.Clear();
        }
        else
        {
            SetBuffer(null, 0, 0);
        }
    }

    public ValueTask SendAsync(Socket socket, ReadOnlyMemory<byte> memory)
    {
        if (BufferList is not null)
        {
            BufferList = null;
        }

        SetBuffer(MemoryMarshal.AsMemory(memory));

        if (socket.SendAsync(this))
        {
            return new ValueTask(this, 0);
        }

        var error = Error;
        return error is not null ? ValueTask.FromException(error) : default;
    }

    private void SetBufferList(in ReadOnlySequence<byte> buffer)
    {
        Debug.Assert(!buffer.IsEmpty);
        Debug.Assert(!buffer.IsSingleSegment);

        if (BufferList is not null)
        {
            BufferList = null;
        }
        else if (!MemoryBuffer.IsEmpty)
        {
            SetBuffer(null, 0, 0);
        }

        _bufferList ??= new List<ArraySegment<byte>>();
        _bufferList.Clear();
        foreach (var b in buffer)
        {
            _bufferList.Add(b.GetArray());
        }

        // 设置此列表的行为, 设定内部缓冲列表中的缓冲
        BufferList = _bufferList;
    }
}

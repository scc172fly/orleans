using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.ObjectPool;

namespace Orleans.Serialization.Buffers.Adaptors
{
    /// <summary>
    /// <see cref="IBufferWriter{T}"/> 的一种实现，用于装箱另一个缓冲区写入器。
    /// </summary>
    public sealed class PooledBufferStream : Stream
    {
        private static readonly ObjectPool<PooledBufferStream> StreamPool = ObjectPool.Create(new PooledStreamPolicy());
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
        private readonly List<byte[]> _segments;
        private readonly int _minAllocationSize;
        private long _length;
        private long _capacity;

        /// <summary>
        /// 初始化 <see cref="PooledBufferStream"/> 类的新实例。
        /// </summary>
        public PooledBufferStream() : this(0)
        { }

        /// <summary>
        /// 初始化 <see cref="PooledBufferStream"/> 类的新实例。
        /// </summary>
        /// <param name="minAllocationSize">分配的最小大小。</param>
        public PooledBufferStream(int minAllocationSize = 0)
        {
            _segments = new();
            _length = 0;
            _minAllocationSize = minAllocationSize > 0 ? minAllocationSize : 4096;
        }

        /// <summary>
        /// 如果池中有可用对象，则从池中获取；否则创建新对象。
        /// </summary>
        /// <returns>一个 <see cref="PooledBufferStream"/>。</returns>
        public static PooledBufferStream Rent() => StreamPool.Get();

        /// <summary>
        /// 将对象归还到池中。
        /// </summary>
        public static void Return(PooledBufferStream stream)
        {
#if NET5_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(stream);
#else
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
#endif
            StreamPool.Return(stream);
        }

        /// <summary>获取已写入的总长度。</summary>
        public override long Length => _length;

        /// <summary>
        /// 以数组形式返回已写入的数据。
        /// </summary>
        /// <returns>已写入的数据。</returns>
        public byte[] ToArray()
        {
            var result = new byte[_length];
            var resultSpan = result.AsSpan();
            var remaining = _length;
            foreach (var buffer in _segments)
            {
                var copyLength = (int)Math.Min(buffer.Length, remaining);
                buffer.AsSpan(0, copyLength).CopyTo(resultSpan);
                resultSpan = resultSpan[copyLength..];
                remaining -= copyLength;
            }

            return result;
        }

        /// <summary>将此写入器的内容复制到另一个写入器。</summary>
        public void CopyTo<TBufferWriter>(ref Writer<TBufferWriter> writer) where TBufferWriter : IBufferWriter<byte>
        {
            var remaining = _length;
            foreach (var buffer in _segments)
            {
                var copyLength = (int)Math.Min(buffer.Length, remaining);
                writer.Write(buffer.AsSpan(0, copyLength));
                remaining -= copyLength;
            }
        }

        /// <summary>
        /// 归还所有租用的缓冲区，并将流重置为空状态。
        /// </summary>
        public void Reset()
        {
            foreach (var buffer in _segments)
            {
                Pool.Return(buffer);
            }

            _segments.Clear();
            _length = 0;
            _capacity = 0;
            Position = 0;
        }

        /// <summary>
        /// 返回一个新的 <see cref="ReadOnlySequence{T}"/>；必须在重置此实例之前使用该序列，并通过 <see cref="ReturnReadOnlySequence"/> 方法归还它。
        /// </summary>
        public ReadOnlySequence<byte> RentReadOnlySequence()
        {
            if (_length == 0)
            {
                return ReadOnlySequence<byte>.Empty;
            }

            if (_segments.Count == 1)
            {
                var buffer = _segments[0];
                return new ReadOnlySequence<byte>(buffer, 0, (int)Math.Min(buffer.Length, _length));
            }

            var runningIndex = 0L;
            BufferSegment? firstSegment = null;
            BufferSegment? previousSegment = null;
            var remaining = _length;
            foreach (var buffer in _segments)
            {
                var segment = BufferSegment.Pool.Get();
                var segmentLength = Math.Min(buffer.Length, remaining);

                segment.Initialize(new ReadOnlyMemory<byte>(buffer, 0, (int)segmentLength), runningIndex);

                runningIndex += segmentLength;
                remaining -= segmentLength;

                previousSegment?.SetNext(segment);

                firstSegment ??= segment;
                previousSegment = segment;
            }

            return new ReadOnlySequence<byte>(firstSegment!, 0, previousSegment!, previousSegment!.Memory.Length);
        }

        /// <summary>
        /// 归还 <see cref="ReadOnlySequence{T}"/>，该序列先前由 <see cref="RentReadOnlySequence"/> 租用；
        /// </summary>
        public void ReturnReadOnlySequence(in ReadOnlySequence<byte> sequence)
        {
            if (sequence.Start.GetObject() is not BufferSegment initialSegment)
            {
                return;
            }

            BufferSegment? segment = initialSegment;
            while (segment is not null)
            {
                var next = segment.Next as BufferSegment;
                BufferSegment.Pool.Return(segment);
                segment = next;
            }
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Position { get; set; }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            var newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (newPosition < 0) throw new InvalidOperationException("Attempted to seek past beginning of stream");
            if (newPosition > Length) throw new InvalidOperationException("Attempted to seek past end of stream");

            Position = newPosition;

            return newPosition;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
#if NET5_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(buffer);
#else
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
#endif
            var destination = buffer.AsSpan(offset, count);
            FindCurrentSegment(out var segmentIndex, out var indexIntoSegment);
            if (segmentIndex < 0)
            {
                return 0;
            }

            var totalRead = 0;
            var remaining = (int)Math.Min(count, _length - Position);

            while (remaining > 0 && segmentIndex < _segments.Count)
            {
                var segment = _segments[segmentIndex];
                var readLength = Math.Min(remaining, Math.Min(segment.Length - indexIntoSegment, destination.Length));
                segment.AsSpan(indexIntoSegment, readLength).CopyTo(destination);

                destination = destination[readLength..];
                remaining -= readLength;
                totalRead += readLength;

                ++segmentIndex;
                indexIntoSegment = 0;
            }

            Position += totalRead;
            return totalRead;
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            if (value == Length)
            {
                // 不执行任何操作。
                return;
            }
            else if (value == 0)
            {
                Reset();
            }
            else
            {
                if (value < Length)
                {
                    // 截断或移除已经写入的缓冲区。
                    var excess = Length - value;
                    while (excess > 0)
                    {
                        var lastSegment = _segments[^1];
                        if (excess >= lastSegment.Length)
                        {
                            // 移除整个分段。
                            excess -= lastSegment.Length;
                            _segments.RemoveAt(_segments.Count - 1);
                            _capacity -= lastSegment.Length;
                            Pool.Return(lastSegment);
                        }
                        else
                        {
                            // 部分截断最后一个分段，无需进行实际操作，
                            // 因为这里只是缩短逻辑长度，而不是物理缓冲区的长度。
                            break;
                        }
                    }
                }
                else
                {
                    // 追加空缓冲区。
                    var deficit = value - Length;
                    while (deficit > 0)
                    {
                        var array = Grow();
                        var length = Math.Min(deficit, array.Length);
                        deficit -= length;
                    }
                }

                _length = value;
                Position = Math.Min(Position, Length);
            }
        }

        private byte[] Grow()
        {
            var array = Pool.Rent(_minAllocationSize);
            _segments.Add(array);
            _capacity += array.Length;
            return array;
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
#if NET5_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(buffer);
#else
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
#endif
            var data = new ReadOnlyMemory<byte>(buffer, offset, count);

            if (Position < Length)
            {
                FindCurrentSegment(out var segmentIndex, out var indexIntoSegment);

                while (Position < Length && data.Length > 0)
                {
                    var writeHead = _segments[segmentIndex].AsMemory(indexIntoSegment);
                    var writeLength = Math.Min(writeHead.Length, data.Length);

                    // 复制数据并更新输入。
                    data[..writeLength].CopyTo(writeHead);
                    data = data[writeLength..];

                    // 更新游标。
                    Position += writeLength;

                    // 前进到下一个分段；
                    ++segmentIndex;
                    indexIntoSegment = 0;
                }
            }

            // 追加剩余数据。
            Append(ref data);
        }

        private void FindCurrentSegment(out int segmentIndex, out int indexIntoSegment)
        {
            segmentIndex = -1;
            indexIntoSegment = -1;
            var segmentStartPos = 0;
            for (var i = 0; i < _segments.Count; i++)
            {
                var currentSegment = _segments[i];

                // 检查当前分段是否包含当前位置。
                if (segmentStartPos + currentSegment.Length > Position)
                {
                    segmentIndex = i;
                    indexIntoSegment = (int)(Position - segmentStartPos);
                    break;
                }

                segmentStartPos += currentSegment.Length;
            }
        }

        private void Append(ref ReadOnlyMemory<byte> data)
        {
            while (data.Length > 0)
            {
                if (_length == _capacity)
                {
                    Grow();
                }

                var writeHead = GetWriteHead();
                var writeLength = Math.Min(writeHead.Length, data.Length);
                data[..writeLength].CopyTo(writeHead);
                data = data[writeLength..];
                _length += writeLength;
            }

            Position = _length;
        }

        /// <inheritdoc/>
        public override void Flush() { }

        private Memory<byte> GetWriteHead() => _segments[^1].AsMemory((int)(_segments[^1].Length - (_capacity - _length)));

        private sealed class PooledStreamPolicy : PooledObjectPolicy<PooledBufferStream>
        {
            public override PooledBufferStream Create() => new();
            public override bool Return(PooledBufferStream obj)
            {
                obj.Reset();
                return true;
            }
        }

        // 仅为测试而设为 internal。
        internal sealed class BufferSegment : ReadOnlySequenceSegment<byte>
        {
            public static readonly ObjectPool<BufferSegment> Pool = ObjectPool.Create(new SegmentPoolPolicy());

            public void Initialize(ReadOnlyMemory<byte> memory, long runningIndex)
            {
                Memory = memory;
                RunningIndex = runningIndex;
            }

            public void SetNext(BufferSegment next) => Next = next;

            public void Reset()
            {
                Memory = default;
                RunningIndex = default;
                Next = default;
            }

            private sealed class SegmentPoolPolicy : PooledObjectPolicy<BufferSegment>
            {
                public override BufferSegment Create() => new();

                public override bool Return(BufferSegment obj)
                {
                    obj.Reset();
                    return true;
                }
            }
        }
    }
}

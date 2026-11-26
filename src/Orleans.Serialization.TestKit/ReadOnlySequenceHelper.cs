using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供创建分隔 <see cref="ReadOnlySequence{T}"/> 实例的帮助。
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class ReadOnlySequenceHelper
    {
        /// <summary>
        /// 将字节的序列分组成最多包含 <paramref name="batchSize"/> 字节的数组。
        /// </summary>
        /// <param name="sequence">组分序列。</param>
        /// <param name="batchSize">每批中最大字节数 。</param>
        /// <returns>字节数组序列,按顺序包含源字节。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sequence"/> 是 <see langword="null"/>.</exception>
        public static IEnumerable<byte[]> Batch(this IEnumerable<byte> sequence, int batchSize)
        {
            if (sequence is null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            return BatchCore(sequence, batchSize);

            static IEnumerable<byte[]> BatchCore(IEnumerable<byte> sequence, int batchSize)
            {
                var batch = new List<byte>(batchSize);
                foreach (var item in sequence)
                {
                    batch.Add(item);

                    if (batch.Count >= batchSize)
                    {
                        yield return batch.ToArray();
                        batch = new List<byte>(batchSize);
                    }
                }

                if (batch.Count > 0)
                {
                    yield return batch.ToArray();
                }
            }
        }

        /// <summary>
        /// 创建只读序列,其各部分由提供的字节数组支持。
        /// </summary>
        /// <param name="buffers">缓冲带应包含序列段。</param>
        /// <returns>一个只读序列,包含按顺序排列的提供的缓冲。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 是 <see langword="null"/>.</exception>
        public static ReadOnlySequence<byte> ToReadOnlySequence(this IEnumerable<byte[]> buffers)
        {
            if (buffers is null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            return CreateReadOnlySequence(buffers.ToArray());
        }

        /// <summary>
        /// 创建只读序列,其各部分由提供的内存区域支持。
        /// </summary>
        /// <param name="buffers">内存区域包含序列段。</param>
        /// <returns>包含按顺序排列的所提供记忆区域的一个只读序列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 是 <see langword="null"/>.</exception>
        public static ReadOnlySequence<byte> ToReadOnlySequence(this IEnumerable<Memory<byte>> buffers)
        {
            if (buffers is null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            return ReadOnlyBufferSegment.Create(buffers);
        }

        /// <summary>
        /// 创建只读序列,其各部分由提供的字节数组支持。
        /// </summary>
        /// <param name="buffers">缓冲带应包含序列段。</param>
        /// <returns>一个只读序列,包含按顺序排列的提供的缓冲。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 是 <see langword="null"/>.</exception>
        public static ReadOnlySequence<byte> CreateReadOnlySequence(params byte[][] buffers)
        {
            if (buffers is null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            if (buffers.Length == 1)
            {
                return new ReadOnlySequence<byte>(buffers[0]);
            }

            var list = new List<Memory<byte>>();
            foreach (var buffer in buffers)
            {
                list.Add(buffer);
            }

            return ToReadOnlySequence(list);
        }

        private class ReadOnlyBufferSegment : ReadOnlySequenceSegment<byte>
        {
            public static ReadOnlySequence<byte> Create(IEnumerable<Memory<byte>> buffers)
            {
                ReadOnlyBufferSegment? segment = null;
                ReadOnlyBufferSegment? first = null;
                foreach (var buffer in buffers)
                {
                    var newSegment = new ReadOnlyBufferSegment
                    {
                        Memory = buffer,
                    };

                    if (segment != null)
                    {
                        segment.Next = newSegment;
                        newSegment.RunningIndex = segment.RunningIndex + segment.Memory.Length;
                    }
                    else
                    {
                        first = newSegment;
                    }

                    segment = newSegment;
                }

                if (first is null)
                {
                    first = segment = new ReadOnlyBufferSegment();
                }

                return new ReadOnlySequence<byte>(first, 0, segment!, segment!.Memory.Length);
            }
        }
    }
}

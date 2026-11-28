using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供用于创建分段 <see cref="ReadOnlySequence{T}"/> 实例的辅助方法。
    /// </summary>
    [ExcludeFromCodeCoverage]
    public static class ReadOnlySequenceHelper
    {
        /// <summary>
        /// 将字节序列分组为最多包含 <paramref name="batchSize"/> 个字节的数组。
        /// </summary>
        /// <param name="sequence">要分批处理的序列。</param>
        /// <param name="batchSize">每批的最大字节数。</param>
        /// <returns>按原顺序包含源字节的字节数组序列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sequence"/> 为 <see langword="null"/>。</exception>
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
        /// 创建只读序列，其分段由给定的字节数组提供底层存储。
        /// </summary>
        /// <param name="buffers">要作为序列分段包含的缓冲区。</param>
        /// <returns>按给定顺序包含缓冲区的只读序列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 为 <see langword="null"/>。</exception>
        public static ReadOnlySequence<byte> ToReadOnlySequence(this IEnumerable<byte[]> buffers)
        {
            if (buffers is null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            return CreateReadOnlySequence(buffers.ToArray());
        }

        /// <summary>
        /// 创建只读序列，其分段由给定的内存区域提供底层存储。
        /// </summary>
        /// <param name="buffers">要作为序列分段包含的内存区域。</param>
        /// <returns>按给定顺序包含内存区域的只读序列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 为 <see langword="null"/>。</exception>
        public static ReadOnlySequence<byte> ToReadOnlySequence(this IEnumerable<Memory<byte>> buffers)
        {
            if (buffers is null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }

            return ReadOnlyBufferSegment.Create(buffers);
        }

        /// <summary>
        /// 创建只读序列，其分段由给定的字节数组提供底层存储。
        /// </summary>
        /// <param name="buffers">要作为序列分段包含的缓冲区。</param>
        /// <returns>按给定顺序包含缓冲区的只读序列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buffers"/> 为 <see langword="null"/>。</exception>
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

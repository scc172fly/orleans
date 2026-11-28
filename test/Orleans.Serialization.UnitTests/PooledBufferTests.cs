using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Xunit;

namespace Orleans.Serialization.UnitTests
{
    /// <summary>
    /// Orleans 的 PooledBuffer 实现测试。
    ///
    /// PooledBuffer 是一个高性能缓冲区管理系统，具有以下特点：
    /// - 使用 ArrayPool 尽可能减少内存分配和 GC 压力
    /// - 支持无需复制的高效切片操作
    /// - 通过分段存储处理大数据量
    /// - 提供对缓冲区内容的零复制访问
    ///
    /// 测试的主要功能：
    /// - 大缓冲区处理（数 MB）
    /// - 各种偏移量下的切片操作
    /// - 内存安全和边界检查
    /// - 正确清理并归还到池中
    ///
    /// 此基础设施对 Orleans 的序列化性能至关重要，
    /// 尤其是在处理大型对象图或流式场景时。
    /// </summary>
    [Trait("Category", "BVT")]
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    public class PooledBufferTests
    {
        [Fact]
        public void LargeBufferRoundTrip()
        {
            var random = new Random();
            var buffer = new PooledBuffer();
            var randomData = new byte[1024 * 1024 * 10];
            random.NextBytes(randomData);
            buffer.Write(randomData);

            var slice4 = buffer.Slice(3000, 1500);
            var sliceArray4 = slice4.ToArray();
            Assert.True(randomData.AsSpan(3000, 1500).SequenceEqual(sliceArray4));

            var slice = buffer.Slice();
            var sliceArray = slice.ToArray();
            Assert.True(randomData.AsSpan().SequenceEqual(sliceArray));

            var slice3 = buffer.Slice(100, 1024);
            var sliceArray3 = slice3.ToArray();
            Assert.True(randomData.AsSpan(100, 1024).SequenceEqual(sliceArray3));

            var slice2 = buffer.Slice(100);
            var sliceArray2 = slice2.ToArray();
            var slicedRandomData = randomData.AsSpan(100).ToArray();
            Assert.True(slicedRandomData.AsSpan().SequenceEqual(sliceArray2));

            var rosArray = new byte[randomData.Length];
            buffer.AsReadOnlySequence().CopyTo(rosArray.AsSpan());
            Assert.True(randomData.AsSpan().SequenceEqual(rosArray));

            var spansArray = new byte[randomData.Length];
            var spansArraySpan = spansArray.AsSpan();
            foreach (var span in buffer.Slice())
            {
                span.CopyTo(spansArraySpan);
                spansArraySpan = spansArraySpan[span.Length..];
            }

            Assert.True(randomData.AsSpan().SequenceEqual(spansArray));

            buffer.Dispose();
        }

        [Fact]
        public void LargeBufferRoundTrip_Single()
        {
            var random = new Random();
            var buffer = new PooledBuffer();
            var randomData = new byte[1024 * 1024 * 10];
            random.NextBytes(randomData);
            buffer.Write(randomData);

            var contents = buffer.ToArray();
            Assert.True(randomData.AsSpan().SequenceEqual(contents));

            buffer.Dispose();
        }

        [Fact]
        public void LargeBufferRoundTrip_ReaderWriter()
        {
            var random = new Random();
            var randomData = new byte[1024 * 1024 * 10];
            random.NextBytes(randomData);
            // 此测试中的原始 reader/writer 操作不访问序列化器会话。
            var writer = Writer.Create(new PooledBuffer(), null!);
            writer.Write(randomData);
            writer.Commit();

            var slice = writer.Output.Slice();
            var sliceReader = Reader.Create(slice, null!);
            var sliceArray = sliceReader.ReadBytes((uint)randomData.Length);
            Assert.True(randomData.AsSpan().SequenceEqual(sliceArray));

            var slice3 = writer.Output.Slice(100, 1024);
            var reader3 = Reader.Create(slice3, null!);
            var result3 = reader3.ReadBytes((uint)slice3.Length);
            Assert.True(randomData.AsSpan(100, 1024).SequenceEqual(result3));

            var slice2 = writer.Output.Slice(100);
            var reader2 = Reader.Create(slice2, null!);
            var result2 = reader2.ReadBytes((uint)slice2.Length);
            Assert.True(randomData.AsSpan(100).SequenceEqual(result2));

            var slice4 = writer.Output.Slice(3000, 1500);
            var reader4 = Reader.Create(slice4, null!);
            var result4 = reader4.ReadBytes((uint)slice4.Length);
            Assert.True(randomData.AsSpan(3000, 1500).SequenceEqual(result4));

            var slice5 = writer.Output.Slice(4500, 125);
            var reader5 = Reader.Create(slice5, null!);
            var result5 = reader5.ReadBytes((uint)slice5.Length);
            Assert.True(randomData.AsSpan(4500, 125).SequenceEqual(result5));

            var ros = writer.Output.AsReadOnlySequence();
            var rosReader = Reader.Create(ros, null!);
            var rosArray = rosReader.ReadBytes((uint)randomData.Length);
            Assert.True(randomData.AsSpan().SequenceEqual(rosArray));

            writer.Dispose();
        }

        /// <summary>
        /// 针对 https://github.com/dotnet/orleans/issues/8503 的回归测试。
        /// </summary>
        [Fact]
        public void PooledBuffer_WriteTwice()
        {
            var serviceProvider = new ServiceCollection()
                .AddSerializer()
                .BuildServiceProvider();
            var pool = serviceProvider.GetRequiredService<SerializerSessionPool>();
            var serializer = serviceProvider.GetRequiredService<Serializer>();
            var obj = LargeObject.BuildRandom();

            SerializeObject(pool, serializer, obj);
            SerializeObject(pool, serializer, obj);

            static void SerializeObject(SerializerSessionPool pool, Serializer serializer, LargeObject obj)
            {
                Writer<PooledBuffer> writer = default;
                var session = pool.GetSession();
                try
                {
                    writer = Writer.CreatePooled(session);
                    serializer.Serialize(obj, ref writer);

                    var sequence = writer.Output.AsReadOnlySequence();
                    Assert.Equal(writer.Output.Length, sequence.Length);
                }
                finally
                {
                    writer.Dispose();
                    session.Dispose();
                }
            }
        }

        /// <summary>
        /// 测试序列化器能否正确序列化 <see cref="PooledBuffer"/>。
        /// </summary>
        [Fact]
        public void PooledBuffer_SerializerRoundTrip()
        {
            var serviceProvider = new ServiceCollection()
                .AddSerializer()
                .BuildServiceProvider();
            var serializer = serviceProvider.GetRequiredService<Serializer>();

            var random = new Random();
            for (var i = 0; i < 10; i++)
            {
                const int TargetLength = 8120;

                // 注意：序列化器负责释放传入的缓冲区，因此我们不释放它。
                var buffer = new PooledBuffer();
                while (buffer.Length < TargetLength)
                {
                    var span = buffer.GetSpan(TargetLength - buffer.Length);
                    var writeLen = Math.Min(span.Length, TargetLength - buffer.Length);
                    random.NextBytes(span[..writeLen]);
                    buffer.Advance(writeLen);
                }

                var bytes = buffer.ToArray();
                Assert.Equal(TargetLength, bytes.Length);

                var result = serializer.Deserialize<PooledBuffer>(serializer.SerializeToArray(buffer));
                Assert.Equal(TargetLength, result.Length);

                var resultBytes = result.ToArray();
                Assert.Equal(bytes, resultBytes);

                // 注意：我们负责释放反序列化返回的缓冲区。
                result.Dispose();
            }
        }

        /// <summary>
        /// 测试当 <see cref="PooledBuffer"/> 嵌入其他结构时，序列化器能否正确序列化它。
        /// </summary>
        [Fact]
        public void PooledBuffer_SerializerRoundTrip_Embedded()
        {
            var serviceProvider = new ServiceCollection()
                .AddSerializer()
                .BuildServiceProvider();
            var serializer = serviceProvider.GetRequiredService<Serializer>();

            var random = new Random();
            for (var i = 0; i < 10; i++)
            {
                const int TargetLength = 8120;

                // 注意：序列化器负责释放传入的缓冲区，因此我们不释放它。
                var buffer = new PooledBuffer();
                while (buffer.Length < TargetLength)
                {
                    var span = buffer.GetSpan(TargetLength - buffer.Length);
                    var writeLen = Math.Min(span.Length, TargetLength - buffer.Length);
                    random.NextBytes(span[..writeLen]);
                    buffer.Advance(writeLen);
                }

                var bytes = buffer.ToArray();
                Assert.Equal(TargetLength, bytes.Length);

                var embed = (Guid: Guid.NewGuid(), Buffer: buffer, Int: 42);
                var result = serializer.Deserialize<(Guid Guid, PooledBuffer Buffer, int Int)>(serializer.SerializeToArray(embed));
                Assert.Equal(embed.Guid, result.Guid);
                Assert.Equal(embed.Int, result.Int);
                var resultBuffer = result.Buffer;
                Assert.Equal(TargetLength, resultBuffer.Length);

                var resultBytes = resultBuffer.ToArray();
                Assert.Equal(bytes, resultBytes);

                // 注意：我们负责释放反序列化返回的缓冲区。
                resultBuffer.Dispose();
            }
        }

        [GenerateSerializer]
        public readonly record struct LargeObject(
            [property: Id(0)] Guid Id,
            [property: Id(1)] (Guid, Guid)[] Values)
        {
            public static LargeObject BuildRandom()
            {
                var id = Guid.NewGuid();
                var values = new (Guid, Guid)[256];

                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = (Guid.NewGuid(), Guid.NewGuid());
                }

                return new(id, values);
            }
        }

        /// <summary>
        /// 确保 BufferSlice 的 SpanEnumerator 和 MemoryEnumerator 正确处理跨越段边界的非零偏移量。
        /// 此测试验证切片从段内某处开始且跨越多个段时，枚举器的偏移量计算。
        /// </summary>
        [Fact]
        public void PooledBuffer_SliceEnumerators_OffsetCrossSegment_Correctness()
        {
            // 准备：写入足够的数据以确保涉及多个段。
            var random = new Random(42);
            var buffer = new PooledBuffer();
            int totalLength = 16 * 1024; // 16 KB，应跨越多个段（最小段大小为 4 KB）。
            var data = new byte[totalLength];
            random.NextBytes(data);
            buffer.Write(data);

            // 选择会跨越段边界的偏移量和长度。
            int offset = 3500; // 未与段边界对齐。
            int length = 7000; // 至少跨越一个段边界。
            var expected = data.AsSpan(offset, length).ToArray();

            // 执行与断言：SpanEnumerator
            var slice = buffer.Slice(offset, length);
            var spanConcat = new byte[length];
            int spanPos = 0;
            foreach (var span in slice)
            {
                span.CopyTo(spanConcat.AsSpan(spanPos));
                spanPos += span.Length;
            }
            Assert.Equal(length, spanPos);
            Assert.Equal(expected, spanConcat);

            var memConcat = new byte[length];
            var memPos = 0;
            foreach (var mem in slice.MemorySegments)
            {
                mem.Span.CopyTo(memConcat.AsSpan(memPos));
                memPos += mem.Length;
            }
            Assert.Equal(length, memPos);
            Assert.Equal(expected, memConcat);

            buffer.Dispose();
        }

        /// <summary>
        /// 确保 BufferSlice 的 SpanEnumerator 和 MemoryEnumerator 执行枚举器位置大于零的代码路径。
        /// 为此使用会跳过至少一个完整段的切片偏移量，使枚举器必须先跳过若干段，再返回数据。
        /// 此测试验证枚举器针对这类非零偏移量返回正确的数据。
        /// </summary>
        [Fact]
        public void PooledBuffer_SliceEnumerators_OffsetAfterFirstSegment_CoversPositionGreaterThanZero()
        {
            // 此测试确保 BufferSlice 的枚举器执行 _position > 0 的分支，
            // 为此使用会跳过至少一个完整段的切片偏移量。输出的正确性
            // 证明 _position 非零时的偏移量计算是正确的。
            var random = new Random(123);
            var buffer = new PooledBuffer();
            int segmentSize = 4 * 1024; // MinimumBlockSize
            int totalLength = segmentSize * 3; // 3 个段
            var data = new byte[totalLength];
            random.NextBytes(data);
            buffer.Write(data);

            // 选择位于第一段之后的偏移量。
            int offset = segmentSize + 123; // 进入第二段的偏移量。
            int length = 1000; // 第二段内任意选定的长度。
            var expected = data.AsSpan(offset, length).ToArray();

            // 执行与断言：SpanEnumerator
            var slice = buffer.Slice(offset, length);
            var spanConcat = new byte[length];
            int spanPos = 0;
            foreach (var span in slice)
            {
                span.CopyTo(spanConcat.AsSpan(spanPos));
                spanPos += span.Length;
            }
            Assert.Equal(length, spanPos);
            Assert.Equal(expected, spanConcat);

            var memConcat = new byte[length];
            var memPos = 0;
            foreach (var mem in slice.MemorySegments)
            {
                mem.Span.CopyTo(memConcat.AsSpan(memPos));
                memPos += mem.Length;
            }
            Assert.Equal(length, memPos);
            Assert.Equal(expected, memConcat);

            buffer.Dispose();
        }

        [Theory]
        [InlineData(PooledBufferLayout.Empty)]
        [InlineData(PooledBufferLayout.SingleUncommitted)]
        [InlineData(PooledBufferLayout.CommittedWithUncommittedTail)]
        [InlineData(PooledBufferLayout.MultipleCommitted)]
        public void DirectSpanEnumerator_MoveNext_TraversesExpectedSegments(PooledBufferLayout layout)
        {
            var buffer = new PooledBuffer();
            try
            {
                var (expectedSegments, _) = CreateLayout(ref buffer, layout);

                {
                    var enumerator = PooledBufferExtensions.GetEnumerator(ref buffer);
                    foreach (var expected in expectedSegments)
                    {
                        Assert.True(enumerator.MoveNext());
                        Assert.True(expected.AsSpan().SequenceEqual(enumerator.Current));
                    }

                    Assert.False(enumerator.MoveNext());
                    Assert.False(enumerator.MoveNext());
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Theory]
        [InlineData(PooledBufferLayout.Empty)]
        [InlineData(PooledBufferLayout.SingleUncommitted)]
        [InlineData(PooledBufferLayout.CommittedWithUncommittedTail)]
        [InlineData(PooledBufferLayout.MultipleCommitted)]
        public void DirectMemoryEnumerator_MoveNext_TraversesExpectedSegments(PooledBufferLayout layout)
        {
            var buffer = new PooledBuffer();
            try
            {
                var (expectedSegments, _) = CreateLayout(ref buffer, layout);

                {
                    var enumerator = buffer.MemorySegments.GetEnumerator();
                    foreach (var expected in expectedSegments)
                    {
                        Assert.True(enumerator.MoveNext());
                        Assert.True(expected.AsSpan().SequenceEqual(enumerator.Current.Span));
                    }

                    Assert.False(enumerator.MoveNext());
                    Assert.False(enumerator.MoveNext());
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Theory]
        [InlineData(PooledBufferLayout.SingleUncommitted)]
        [InlineData(PooledBufferLayout.CommittedWithUncommittedTail)]
        public void DirectSpanEnumerator_CurrentAliasesUnderlyingStorage(PooledBufferLayout layout)
        {
            var buffer = new PooledBuffer();
            try
            {
                var (expectedSegments, retainedMemory) = CreateLayout(ref buffer, layout);

                {
                    var enumerator = PooledBufferExtensions.GetEnumerator(ref buffer);
                    Assert.True(enumerator.MoveNext());
                    var current = enumerator.Current;
                    Assert.True(expectedSegments[0].AsSpan().SequenceEqual(current));

                    retainedMemory.Span[1] = 0x7F;

                    Assert.Equal((byte)0x7F, current[1]);
                    Assert.Equal(expectedSegments[0][0], current[0]);
                    Assert.Equal(expectedSegments[0][2], current[2]);
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Theory]
        [InlineData(PooledBufferLayout.SingleUncommitted)]
        [InlineData(PooledBufferLayout.CommittedWithUncommittedTail)]
        public void DirectMemoryEnumerator_CurrentAliasesUnderlyingStorage(PooledBufferLayout layout)
        {
            var buffer = new PooledBuffer();
            try
            {
                var (expectedSegments, retainedMemory) = CreateLayout(ref buffer, layout);

                {
                    var enumerator = buffer.MemorySegments.GetEnumerator();
                    Assert.True(enumerator.MoveNext());
                    var current = enumerator.Current;
                    Assert.True(expectedSegments[0].AsSpan().SequenceEqual(current.Span));

                    retainedMemory.Span[1] = 0x7F;

                    Assert.Equal((byte)0x7F, current.Span[1]);
                    Assert.Equal(expectedSegments[0][0], current.Span[0]);
                    Assert.Equal(expectedSegments[0][2], current.Span[2]);
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private static (byte[][] ExpectedSegments, Memory<byte> RetainedMemory) CreateLayout(
            ref PooledBuffer buffer,
            PooledBufferLayout layout)
        {
            byte[] firstSegment = [0x11, 0x12, 0x13];
            byte[] secondSegment = [0x21, 0x22, 0x23, 0x24];
            byte[] tail = [0x31, 0x32];

            if (layout == PooledBufferLayout.Empty)
            {
                return ([], Memory<byte>.Empty);
            }

            var retainedMemory = WriteUncommitted(ref buffer, firstSegment);
            if (layout == PooledBufferLayout.SingleUncommitted)
            {
                return ([firstSegment], retainedMemory);
            }

            var nextMemory = buffer.GetMemory(retainedMemory.Length - firstSegment.Length);
            if (layout == PooledBufferLayout.CommittedWithUncommittedTail)
            {
                tail.CopyTo(nextMemory.Span);
                buffer.Advance(tail.Length);
                return ([firstSegment, tail], retainedMemory);
            }

            secondSegment.CopyTo(nextMemory.Span);
            buffer.Advance(secondSegment.Length);
            _ = buffer.GetMemory(nextMemory.Length - secondSegment.Length);
            return ([firstSegment, secondSegment], retainedMemory);

            static Memory<byte> WriteUncommitted(ref PooledBuffer buffer, byte[] content)
            {
                var memory = buffer.GetMemory();
                content.CopyTo(memory.Span);
                buffer.Advance(content.Length);
                return memory;
            }
        }

        public enum PooledBufferLayout
        {
            Empty,
            SingleUncommitted,
            CommittedWithUncommittedTail,
            MultipleCommitted
        }
    }
}

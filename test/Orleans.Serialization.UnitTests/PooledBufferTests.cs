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
    /// 执行 Orleans' PooledBuffer 的测试。
    ///
    /// PooledBuffer 是一个高绩效缓冲管理系统,它:
    /// - 使用 ArrayPool 来尽量减少拨款和GC压力
    /// - 支持高效的切片操作,不复制
    /// - 通过分部分储存处理大量数据
    /// - 提供缓冲区内内容的零副本
    ///
    /// 测试的关键特征:
    /// - 大型缓冲处理(多兆字节)
    /// - 各种冲抵的切断作业
    /// - 内存安全和约束检查
    /// - 适当清理和重新池水
    ///
    /// 这一基础设施对于 Orleans的序列化绩效至关重要,
    /// 特别是当处理大对象图形或流式情景时。
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
            // 本测试的原始阅读/撰写操作无法访问连载器会话。
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
        /// https://github.com/dotnet/orleans/issues/8503的回归测试
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
        /// 测试序列器能够正确序列 <see cref="PooledBuffer"/>。
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

                // 注:序列器负责释放向其提供的缓冲,所以我们不放出这个。
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

                // 注:我们负责处置从消毒中返回的缓冲。
                result.Dispose();
            }
        }

        /// <summary>
        /// 测试序列器嵌入另一个结构时, 序列器能够正确序列 <see cref="PooledBuffer"/> 。
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

                // 注:序列器负责释放向其提供的缓冲,所以我们不放出这个。
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

                // 注:我们负责处置从消毒中返回的缓冲。
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
        /// 确保 BufferSlice的 SpanEnumerator 和 MemoryEnumerator 正确处理跨越段界的非零抵消。
        /// 当切片从一个段段的部位开始,跨多个段段时,此测试会为计数员练习偏移数学。
        /// </summary>
        [Fact]
        public void PooledBuffer_SliceEnumerators_OffsetCrossSegment_Correctness()
        {
            // 排列: 写入足够数据以确保多个部分
            var random = new Random(42);
            var buffer = new PooledBuffer();
            int totalLength = 16 * 1024; // 16KB, 应横跨多个区段( 4KB 分钟段大小)
            var data = new byte[totalLength];
            random.NextBytes(data);
            buffer.Write(data);

            // 选择一个偏移和长度,跨段边界
            int offset = 3500; // 未与段边界对齐
            int length = 7000; // 跨越至少一个段边界
            var expected = data.AsSpan(offset, length).ToArray();

            // 动作和发信人: SpanEnumerator
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
        /// 确保: BufferSlice's SpanEnumerator 和 MemoryEnumerator 运行数值表示器位置大于零的代码路径。
        /// 实现这一点的方法是使用切片偏移法,至少跳过一个整段,因此,在生成数据之前,该查点器必须跳过段。
        /// 测试证实,这些测量员返回了这种非零抵消的正确数据。
        /// </summary>
        [Fact]
        public void PooledBuffer_SliceEnumerators_OffsetAfterFirstSegment_CoversPositionGreaterThanZero()
        {
            // 此测试确保 BufferSlice 计数员在 _位置 > 0处运行分支
            // 使用切片偏移,至少跳过一个整段。输出的正确性
            // 校验偏移数学对非零位置正确。
            var random = new Random(123);
            var buffer = new PooledBuffer();
            int segmentSize = 4 * 1024; // MinimumBlockSize
            int totalLength = segmentSize * 3; // 3个部分
            var data = new byte[totalLength];
            random.NextBytes(data);
            buffer.Write(data);

            // 选择在第一部分之后的偏移
            int offset = segmentSize + 123; // 移位到第二部分
            int length = 1000; // 第二部分的任意长度
            var expected = data.AsSpan(offset, length).ToArray();

            // 动作和发信人: SpanEnumerator
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

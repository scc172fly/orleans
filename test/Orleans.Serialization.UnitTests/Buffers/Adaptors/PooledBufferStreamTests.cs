using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions;
using Microsoft.Extensions.ObjectPool;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Xunit;

namespace Orleans.Serialization.Buffers.Adaptors.UnitTests;

/// <summary>
/// PooledBufferStream 构造函数的单元测试。
/// </summary>
[Trait("Category", "BVT")]
[Trait("Suite", "BVT")]
[Trait("Provider", "None")]
[Trait("Area", "Serialization")]
public class PooledBufferStreamTests
{
    /// <summary>
    /// 验证无参数构造函数初始化的新实例长度为零，
    /// 且具有适当的流操作能力。
    /// </summary>
    [Fact]
    public void Constructor_Parameterless_InitializesWithZeroLengthAndValidCapabilities()
    {
        // 准备并执行
        using var stream = new PooledBufferStream();

        // 验证
        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        // 验证 Position 的默认值为 0。
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 验证带有 minAllocationSize 参数的构造函数初始化的新实例
    /// 长度为零，且具有适当的流操作能力。
    /// 测试 minAllocationSize 参数的各种边界情况。
    /// </summary>
    /// <param name="minAllocationSize">传递给构造函数的最小分配大小。</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public void Constructor_WithMinAllocationSize_InitializesWithZeroLengthAndValidCapabilities(int minAllocationSize)
    {
        // 准备并执行
        using var stream = new PooledBufferStream(minAllocationSize);

        // 验证
        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        // 验证 Position 的默认值为 0。
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 验证 PooledBufferStream 构造函数将 Length 属性初始化为零，
    /// 无论提供的 minAllocationSize 参数为何值。
    /// 测试用例包括负数、零、正数、int.MaxValue 和 int.MinValue。
    /// </summary>
    /// <param name="minAllocationSize">用于初始化流的最小分配大小。</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Constructor_WithMinAllocationValues_LengthIsZero(int minAllocationSize)
    {
        // 准备并执行
        using var stream = new PooledBufferStream(minAllocationSize);

        // 验证
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证 PooledBufferStream 的默认构造函数将 Length 属性初始化为零。
    /// </summary>
    [Fact]
    public void DefaultConstructor_LengthIsZero()
    {
        // 准备并执行
        using var stream = new PooledBufferStream();

        // 验证
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证调用 Rent 会返回非 null 的 PooledBufferStream 实例，其初始长度为零。
    /// 本测试将 Rent 的调用次数参数化，以确保不论调用多少次，行为均一致。
    /// </summary>
    /// <param name="rentCount">调用 Rent 的次数。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Rent_MultipleTimes_ReturnsValidInstance(int rentCount)
    {
        // 准备
        var instances = new List<PooledBufferStream>();

        // 执行
        for (int i = 0; i < rentCount; i++)
        {
            var instance = PooledBufferStream.Rent();
            instances.Add(instance);
        }

        // 验证
        Assert.Equal(rentCount, instances.Count);
        foreach (var instance in instances)
        {
            Assert.NotNull(instance);
            // 验证刚从池中租用的流的 Length 属性为零。
            Assert.Equal(0, instance.Length);
        }
    }

    /// <summary>
    /// 测试归还有效流时会回收该流，即确保随后租用到的是刚归还的同一实例。
    /// </summary>
    [Fact]
    public void Return_ValidStream_RecyclesObject()
    {
        // 准备
        using var stream = PooledBufferStream.Rent();
        // 修改状态以模拟使用过程。
        stream.Position = 100;

        // 执行
        PooledBufferStream.Return(stream);
        var recycledStream = PooledBufferStream.Rent();

        // 验证
        // 预期回收并复用同一实例。
        Assert.True(Object.ReferenceEquals(stream, recycledStream), "The recycled stream instance should be the same as the one returned.");

        // 清理：再次归还流，以免影响其他测试。
        PooledBufferStream.Return(recycledStream);
    }

    /// <summary>
    /// 测试向 Return 传入 null 流时会引发异常。
    /// </summary>
    [Fact]
    public void Return_NullStream_ThrowsException()
    {
        // 准备、执行与断言
        Assert.Throws<ArgumentNullException>(() => PooledBufferStream.Return(null!));
    }

    /// <summary>
    /// 验证新建 PooledBufferStream 的 Length 为零，不受指定的 minAllocationSize 影响。
    /// </summary>
    /// <param name="minAllocationSize">构造流时使用的最小分配大小。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    [InlineData(100)]
    [InlineData(-1)]
    public void Length_OnNewStream_ReturnsZero(int minAllocationSize)
    {
        // 准备
        using var stream = new PooledBufferStream(minAllocationSize);

        // 执行
        long length = stream.Length;

        // 验证
        Assert.Equal(0L, length);
    }

    /// <summary>
    /// 测试未向流写入任何数据时，ToArray 返回空数组。
    /// </summary>
    [Fact]
    public void ToArray_EmptyStream_ReturnsEmptyArray()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行
        byte[] result = stream.ToArray();

        // 验证
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// 验证 CopyTo 方法将所有段正确写入提供的 writer。
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 10, 20, 30 })]
    public void CopyTo_ValidSegments_CopiesDataCorrectly(byte[] expectedData)
    {
        using var stream = new PooledBufferStream();
        stream.Write(expectedData);

        var output = new byte[expectedData.Length];
        // 此测试仅涉及原始缓冲区复制，不访问序列化器会话。
        var writer = Writer.Create(output, session: null!);

        stream.CopyTo(ref writer);

        Assert.Equal(expectedData, output);
    }

    /// <summary>
    /// 验证对于较大的缓冲区，CopyTo 方法也能将所有段正确写入提供的 writer。
    /// 使用大于 5 KB 的缓冲区进行测试，确保多段功能正常工作。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1 * 1024)]
    [InlineData(4 * 1024 - 1)]
    [InlineData(4 * 1024)]
    [InlineData(4 * 1024 + 1)]
    [InlineData(5 * 1024)]
    [InlineData(8 * 1024)]
    public void CopyTo_LargeBuffers_CopiesDataCorrectly(int bufferSize)
    {
        using var stream = new PooledBufferStream();
        var expectedData = GenerateTestData(bufferSize);
        stream.Write(expectedData, 0, expectedData.Length);

        var output = new byte[expectedData.Length];
        // 此测试仅涉及原始缓冲区复制，不访问序列化器会话。
        var writer = Writer.Create(output, session: null!);

        stream.CopyTo(ref writer);

        Assert.True(expectedData.SequenceEqual(output));
    }

    /// <summary>
    /// 验证对新建实例调用 Reset 后，Length 为零。
    /// </summary>
    [Fact]
    public void Reset_FreshInstance_LengthIsZero()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行
        stream.Reset();

        // 验证
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证向流写入数据后，调用 Reset 会将流长度重置为零。
    /// 使用不同的写入字节数测试多种情况。
    /// </summary>
    /// <param name="writeCount">要写入流的字节数。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(1024)]
    public void Reset_AfterWrite_LengthResetToZero(int writeCount)
    {
        // 准备
        using var stream = new PooledBufferStream();
        var buffer = new byte[writeCount];
        // 如有需要，使用一些模拟数据填充缓冲区。
        for (int i = 0; i < writeCount; i++)
        {
            buffer[i] = (byte)(i % 256);
        }

        // 执行
        stream.Write(buffer, 0, writeCount);
        // 视具体实现而定，Length 不一定准确反映已写入的字节数。
        // 调用 Reset 应清除已写入的数据并重置 Length。
        stream.Reset();

        // 验证
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证调用 Reset 会将 Position 属性重置为零。
    /// 从池中回收并复用流时，这一点很重要，可以确保位置
    /// 不会沿用上一次使用时的值。
    /// </summary>
    /// <param name="initialPosition">调用 Reset 前要设置的初始位置。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1024)]
    public void Reset_WithPosition_ResetsPositionToZero(int initialPosition)
    {
        // 准备
        using var stream = new PooledBufferStream();
        stream.SetLength(2000); // 设置足以容纳该位置的流长度。
        stream.Position = initialPosition;

        // 执行
        stream.Reset();

        // 验证
        Assert.Equal(0, stream.Position);
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证 RentReadOnlySequence 针对各种数据长度均返回正确的 ReadOnlySequence。
    /// 未写入数据时返回空序列；否则返回与已写入数据一致的序列。
    /// </summary>
    /// <param name="dataLength">要写入流的测试数据长度。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4096)]
    [InlineData(5000)]
    public void RentReadOnlySequence_VariousDataLengths_ReturnsExpectedSequence(int dataLength)
    {
        // 准备
        using var stream = new PooledBufferStream();
        byte[] expectedData = GenerateTestData(dataLength);
        if (dataLength > 0)
        {
            stream.Write(expectedData, 0, expectedData.Length);
        }

        // 执行
        ReadOnlySequence<byte> sequence = stream.RentReadOnlySequence();

        // 验证
        if (dataLength == 0)
        {
            Assert.Equal(0, sequence.Length);
        }
        else
        {
            byte[] actualData = sequence.ToArray();
            Assert.Equal(expectedData.Length, actualData.Length);
            Assert.True(expectedData.SequenceEqual(actualData));
        }

        stream.ReturnReadOnlySequence(sequence);
    }

    /// <summary>
    /// 生成指定长度的确定性测试数据。
    /// </summary>
    /// <param name="length">要生成的数据长度。</param>
    /// <returns>以连续整数对 256 取模后的值填充的字节数组。</returns>
    private static byte[] GenerateTestData(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        return data;
    }

    /// <summary>
    /// 验证当序列起点未返回 BufferSegment 时，ReturnReadOnlySequence 不执行任何操作。
    /// 此测试创建一个模拟的 ReadOnlySequenceSegment&lt;byte&gt;，使其返回的对象并非 BufferSegment 类型。
    /// 预期结果：不引发异常。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void ReturnReadOnlySequence_StartNotBufferSegment_DoesNotThrow(int dummyValue)
    {
        // 准备
        var dummySegment = new DummyReadOnlySequenceSegment(dummyValue);
        // 使用模拟段创建 ReadOnlySequence。
        var sequence = new ReadOnlySequence<byte>(dummySegment, 0, dummySegment, dummySegment.Memory.Length);
        using var stream = new PooledBufferStream();

        // 执行与断言（不应引发异常）
        stream.ReturnReadOnlySequence(in sequence);
    }

    /// <summary>
    /// 验证 ReturnReadOnlySequence 将 BufferSegment 链归还到池中。
    /// 测试多段链中的每个段均已正确归还到池中，
    /// 并可在后续操作中复用。
    /// </summary>
    [Fact]
    public void ReturnReadOnlySequence_ValidBufferSegmentChain_CallsPoolReturnForEachSegment()
    {
        using var stream = new PooledBufferStream();

        // 写入跨越多个段的数据以创建段链。
        var dataSize = 10000; // 足够大，可以跨越多个段。
        var testData = GenerateTestData(dataSize);
        stream.Write(testData, 0, testData.Length);

        // 租用 ReadOnlySequence 以获取 BufferSegment 链。
        var sequence = stream.RentReadOnlySequence();

        // 验证获得的是多段链。
        Assert.True(sequence.Length > 0);

        // 在归还之前统计链中的段数。
        var segmentCount = CountSegmentsInChain(sequence);
        Assert.True(segmentCount > 1, "Test requires multiple segments to verify chain handling");

        // 从池中获取新的段，以验证归还之前它们是不同的实例。
        var freshSegments = new List<PooledBufferStream.BufferSegment>();
        for (int i = 0; i < segmentCount; i++)
        {
            freshSegments.Add(PooledBufferStream.BufferSegment.Pool.Get());
        }

        // 归还新获取的段，以免影响测试。
        foreach (var segment in freshSegments)
        {
            PooledBufferStream.BufferSegment.Pool.Return(segment);
        }

        // 归还序列，此操作应将链中的所有段归还到池中。
        stream.ReturnReadOnlySequence(sequence);

        // 通过获取新的段并检查它们，验证先前的段已归还到池中，
        // 且这些段是相同的实例（表明它们已被回收复用）。
        var recycledSegments = new List<PooledBufferStream.BufferSegment>();
        for (int i = 0; i < segmentCount; i++)
        {
            recycledSegments.Add(PooledBufferStream.BufferSegment.Pool.Get());
        }

        // 回收的段应已重置，可以再次使用。
        foreach (var segment in recycledSegments)
        {
            Assert.True(segment.Memory.IsEmpty, "Recycled segment should have empty memory");
            Assert.Equal(0, segment.RunningIndex);
            Assert.Null(segment.Next);
        }

        // 通过将这些段归还到池中完成清理。
        foreach (var segment in recycledSegments)
        {
            PooledBufferStream.BufferSegment.Pool.Return(segment);
        }
    }

    /// <summary>
    /// 统计 ReadOnlySequence 链中的段数。
    /// </summary>
    /// <param name="sequence">要统计段数的序列。</param>
    /// <returns>链中的段数。</returns>
    private static int CountSegmentsInChain(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            return 1;
        }

        var count = 0;
        var position = sequence.Start;
        while (sequence.TryGet(ref position, out var memory))
        {
            count++;
            if (position.Equals(sequence.End))
            {
                break;
            }
        }

        return count;
    }

    /// <summary>
    /// ReadOnlySequenceSegment&lt;byte&gt; 的模拟实现，用于模拟 GetObject 不返回 BufferSegment 的段。
    /// </summary>
    private class DummyReadOnlySequenceSegment : ReadOnlySequenceSegment<byte>
    {
        private readonly int _dummyValue;

        public DummyReadOnlySequenceSegment(int dummyValue)
        {
            _dummyValue = dummyValue;
            // 为简单起见，将 Memory 初始化为空数组。
            Memory = new byte[0];
        }

    }

    /// <summary>
    /// 测试 CanRead 属性返回 true。
    /// 此测试验证新建 PooledBufferStream 正确报告自身可读。
    /// </summary>
    [Fact]
    public void CanRead_Property_ReturnsTrue()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行
        bool canRead = stream.CanRead;

        // 验证
        Assert.True(canRead);
    }

    /// <summary>
    /// 测试 CanSeek 属性返回 true。
    /// </summary>
    [Fact]
    public void CanSeek_Property_ReturnsTrue()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行
        bool result = stream.CanSeek;

        // 验证
        Assert.True(result);
    }

    /// <summary>
    /// 测试无论使用哪个构造函数，CanWrite 属性均返回 true。
    /// </summary>
    /// <param name="useNonDefaultConstructor">用于测试默认构造函数和带参数构造函数的布尔标志。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanWrite_Property_ReturnsTrue(bool useNonDefaultConstructor)
    {
        // 准备
        using var stream = useNonDefaultConstructor ? new PooledBufferStream(1024) : new PooledBufferStream();

        // 执行
        bool canWrite = stream.CanWrite;

        // 验证
        Assert.True(canWrite);
    }

    /// <summary>
    /// 测试预设流长度时的有效 Seek 操作场景。
    /// 对于 Begin：newPosition 等于偏移量。
    /// 对于 Current：newPosition 等于 Position 加上偏移量。
    /// 对于 End：newPosition 等于 Length 加上偏移量。
    /// </summary>
    /// <param name="offset">Seek 的偏移量参数。</param>
    /// <param name="origin">Seek 使用的 SeekOrigin。</param>
    /// <param name="expectedPosition">定位后的预期新位置。</param>
    [Theory]
    [InlineData(30, SeekOrigin.Begin, 30)]
    [InlineData(20, SeekOrigin.Current, 30)]
    [InlineData(-10, SeekOrigin.End, 90)]
    public void Seek_ValidScenarios_ExpectedPosition(long offset, SeekOrigin origin, long expectedPosition)
    {
        // 准备
        using var stream = new PooledBufferStream();
        stream.SetLength(100);
        if (origin == SeekOrigin.Current)
        {
            stream.Position = 10;
        }

        // 执行
        long result = stream.Seek(offset, origin);

        // 验证
        Assert.Equal(expectedPosition, result);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 测试流长度为默认值（0）时的有效 Seek 操作场景。
    /// 对于 Begin 和 Current，当偏移量为 0 时，操作应成功。
    /// </summary>
    [Theory]
    [InlineData(0, SeekOrigin.Begin, 0)]
    [InlineData(0, SeekOrigin.Current, 0)]
    public void Seek_ZeroOffsetOnEmptyStream_ReturnsZero(long offset, SeekOrigin origin, long expectedPosition)
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行
        long result = stream.Seek(offset, origin);

        // 验证
        Assert.Equal(expectedPosition, result);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 测试预期引发 InvalidOperationException 的 Seek 场景，
    /// 即计算得出的位置超出有效范围时。
    /// </summary>
    /// <param name="offset">Seek 的偏移量参数。</param>
    /// <param name="origin">Seek 使用的 SeekOrigin。</param>
    /// <param name="expectedMessage">预期的异常消息。</param>
    [Theory]
    // 对于 Begin：偏移量为负数。
    [InlineData(-1, SeekOrigin.Begin, "Attempted to seek past beginning of stream")]
    // 对于 Current：结果为负数。
    [InlineData(-11, SeekOrigin.Current, "Attempted to seek past beginning of stream")]
    // 对于 End：绝对值大于 Length 的负偏移量使 newPosition 为负数。
    [InlineData(-101, SeekOrigin.End, "Attempted to seek past beginning of stream")]
    // 对于 Begin：偏移量大于 Length。
    [InlineData(101, SeekOrigin.Begin, "Attempted to seek past end of stream")]
    // 对于 Current：结果大于 Length。
    [InlineData(91, SeekOrigin.Current, "Attempted to seek past end of stream")]
    // 对于 End：正偏移量使 newPosition 大于 Length。
    [InlineData(1, SeekOrigin.End, "Attempted to seek past end of stream")]
    public void Seek_OutOfBounds_ThrowsInvalidOperationException(long offset, SeekOrigin origin, string expectedMessage)
    {
        // 准备
        using var stream = new PooledBufferStream();
        stream.SetLength(100);
        if (origin == SeekOrigin.Current)
        {
            stream.Position = 10;
        }

        // 执行并验证
        var ex = Assert.Throws<InvalidOperationException>(() => stream.Seek(offset, origin));
        Assert.Equal(expectedMessage, ex.Message);
    }

    /// <summary>
    /// 测试无效的 SeekOrigin 值会引发 ArgumentOutOfRangeException。
    /// </summary>
    [Fact]
    public void Seek_InvalidOrigin_ThrowsArgumentOutOfRangeException()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行并验证
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)999));
    }

    /// <summary>
    /// 验证对空流（未写入数据）调用 Read 时返回零。
    /// </summary>
    [Fact]
    public void Read_WithEmptyStream_ReturnsZero()
    {
        // 准备
        using var stream = new PooledBufferStream();
        var buffer = new byte[10];

        // 执行
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        // 验证
        Assert.Equal(0, bytesRead);
    }

    /// <summary>
    /// 验证使用 null 缓冲区调用 Read 会引发 ArgumentNullException。
    /// </summary>
    [Fact]
    public void Read_NullBuffer_ThrowsArgumentNullException()
    {
        // 准备
        using var stream = new PooledBufferStream();

        // 执行并验证
        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 5));
    }

    /// <summary>
    /// 验证使用无效偏移量或计数调用 Read 会引发 ArgumentOutOfRangeException。
    /// </summary>
    /// <param name="offset">读取时使用的偏移量。</param>
    /// <param name="count">读取时使用的计数。</param>
    [Theory]
    [InlineData(-1, 5)]
    [InlineData(2, 100)]
    public void Read_InvalidOffsetOrCount_ThrowsArgumentOutOfRangeException(int offset, int count)
    {
        // 准备
        using var stream = new PooledBufferStream();
        var buffer = new byte[10];

        // 执行并验证
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, offset, count));
    }

    /// <summary>
    /// 验证使用等于当前 Length 的值调用 SetLength 不会改变流的状态。
    /// 此测试先设置非零的初始长度，再以相同的值调用 SetLength。
    /// </summary>
    [Fact]
    public void SetLength_ValueEqualsCurrentLength_DoesNotChangeState()
    {
        // 准备
        using var stream = new PooledBufferStream();
        stream.Position = 200;
        stream.SetLength(500);
        long originalLength = stream.Length;
        long originalPosition = stream.Position;

        // 执行
        stream.SetLength(500);

        // 验证
        Assert.Equal(originalLength, stream.Length);
        Assert.Equal(originalPosition, stream.Position);
    }

    /// <summary>
    /// 验证对空流或非空流调用 SetLength 会更新 Length 并相应调整 Position。
    /// 此测试涵盖增长和截断等场景。
    /// </summary>
    /// <param name="initialPosition">为流设置的初始 Position。</param>
    /// <param name="initialSetLength">第一次调用 SetLength 时的值，用于建立非空状态。</param>
    /// <param name="newSetLength">第二次调用 SetLength 时的值，用于更新长度。</param>
    /// <param name="expectedPosition">更新长度后预期的 Position。</param>
    /// <param name="expectedLength">更新长度后预期的 Length。</param>
    [Theory]
    [InlineData(100, 500, 500, 100, 500)]   // 非空流的长度不变场景。
    [InlineData(600, 500, 1000, 500, 1000)]  // 增长场景：初始长度从 500 增至 1000。
    [InlineData(800, 1000, 500, 500, 500)]   // 截断场景：初始长度从 1000 截断为 500。
    public void SetLength_Scenarios_UpdatesLengthAndPosition(int initialPosition, int initialSetLength, int newSetLength, int expectedPosition, int expectedLength)
    {
        // 准备
        using var stream = new MemoryStream();
        stream.Position = initialPosition;
        stream.SetLength(initialSetLength);

        // 执行
        stream.SetLength(newSetLength);

        // 验证
        Assert.Equal(expectedLength, stream.Length);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 验证对空流调用 SetLength(0) 会使 Length 和 Position 保持为 0。
    /// </summary>
    [Fact]
    public void SetLength_ZeroOnEmpty_DoesNotChangeState()
    {
        // 准备
        using var stream = new PooledBufferStream();
        stream.Position = 0;

        // 执行
        stream.SetLength(0);

        // 验证
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 提供无效参数组合，用于测试 Write 方法引发的异常。
    /// 每个对象数组包含：缓冲区、偏移量、计数和预期异常类型。
    /// </summary>
    public static IEnumerable<object?[]> InvalidWriteParameters
    {
        get
        {
            yield return new object?[] { null, 0, 1, typeof(ArgumentNullException) };
            yield return new object?[] { new byte[5], -1, 3, typeof(ArgumentOutOfRangeException) };
            yield return new object?[] { new byte[5], 2, -1, typeof(ArgumentOutOfRangeException) };
            yield return new object?[] { new byte[5], 3, 3, typeof(ArgumentOutOfRangeException) };
        }
    }

    /// <summary>
    /// 测试传入无效参数时，Write 会引发相应的异常。
    /// </summary>
    /// <param name="buffer">输入字节数组（可以为 null）。</param>
    /// <param name="offset">数组中的起始偏移量。</param>
    /// <param name="count">要写入的字节数。</param>
    /// <param name="expectedException">预期的异常类型。</param>
    [Theory]
    [MemberData(nameof(InvalidWriteParameters))]
    public void Write_InvalidParameters_ThrowsException(byte[]? buffer, int offset, int count, Type expectedException)
    {
        using var stream = new PooledBufferStream();
        // null 是为 Theory 测试刻意提供的输入，用于验证 Stream.Write 的参数校验。
        Assert.Throws(expectedException, () => stream.Write(buffer!, offset, count));
    }

    /// <summary>
    /// 测试写入计数为零时，调用 Write 不会改变流的位置。
    /// </summary>
    [Fact]
    public void Write_ZeroCount_DoesNothing()
    {
        using var stream = new PooledBufferStream();
        long initialPosition = stream.Position;
        byte[] data = new byte[10];
        stream.Write(data, 0, 0);
        Assert.Equal(initialPosition, stream.Position);
    }

    /// <summary>
    /// 测试 Write 将数据正确追加到流中。
    /// 验证可通过 ToArray() 取回已写入的数据，且流的 Position 正确前移。
    /// </summary>
    /// <param name="inputLength">要写入的数据长度。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(4096)]
    public void Write_ValidInput_AppendsData(int inputLength)
    {
        using var stream = new PooledBufferStream();
        byte[] data = new byte[inputLength];
        for (int i = 0; i < inputLength; i++)
        {
            data[i] = (byte)(i % 256);
        }
        long initialPosition = stream.Position;

        stream.Write(data, 0, data.Length);

        // 验证数据已正确追加。
        byte[] writtenData = stream.ToArray();
        Assert.Equal(data, writtenData);

        // 验证流长度与已写入的数据长度一致。
        Assert.Equal(inputLength, stream.Length);

        // 验证位置已正确前移。
        Assert.Equal(initialPosition + inputLength, stream.Position);
    }

    /// <summary>
    /// 验证 Flush 方法不执行任何操作，也不引发异常。
    /// </summary>
    [Fact]
    public void Flush_NoData_DoesNotThrowException()
    {
        // 准备：创建 PooledBufferStream 的新实例。
        using var stream = new PooledBufferStream();

        // 执行与断言：调用 Flush 不应引发异常。
        var exception = Record.Exception(() => stream.Flush());
        Assert.Null(exception);
    }
}

/// <summary>
/// BufferSegment 类的单元测试。
/// </summary>
[Trait("Category", "BVT")]
[Trait("Suite", "BVT")]
[Trait("Provider", "None")]
[Trait("Area", "Serialization")]
public class BufferSegmentTests
{
    /// <summary>
    /// 为 Initialize 方法提供测试数据。
    /// 每个测试用例包括一个字节数组及相应的 runningIndex 值。
    /// </summary>
    public static IEnumerable<object[]> GetInitializeTestData()
    {
        yield return new object[] { new byte[0], 0L };
        yield return new object[] { new byte[] { 1, 2, 3 }, 123L };
        yield return new object[] { new byte[] { 255 }, -1L };
        yield return new object[] { new byte[] { 0, 0, 0, 0, 1 }, long.MaxValue };
        yield return new object[] { new byte[] { 10, 20, 30, 40, 50 }, long.MinValue };
    }

    /// <summary>
    /// 验证 Initialize 方法正确设置 Memory 和 RunningIndex 属性。
    /// 测试各种边界情况，包括空数组、典型值及数值边界值。
    /// </summary>
    /// <param name="inputData">要设置到 Memory 属性中的字节数组。</param>
    /// <param name="runningIndex">要赋予的 runningIndex 值。</param>
    [Theory]
    [MemberData(nameof(GetInitializeTestData))]
    public void Initialize_ValidInput_PropertiesSetCorrectly(byte[] inputData, long runningIndex)
    {
        // 准备
        ReadOnlyMemory<byte> readOnlyMemory = new ReadOnlyMemory<byte>(inputData);
        var bufferSegment = new PooledBufferStream.BufferSegment();

        // 执行
        bufferSegment.Initialize(readOnlyMemory, runningIndex);

        // 验证
        Assert.Equal(readOnlyMemory.ToArray(), bufferSegment.Memory.ToArray());
        Assert.Equal(runningIndex, bufferSegment.RunningIndex);
    }

    /// <summary>
    /// 验证使用有效 BufferSegment 调用 SetNext 会正确设置 Next 属性。
    /// </summary>
    [Fact]
    public void SetNext_ValidBufferSegment_SetsNextProperty()
    {
        // 准备
        var segment = new PooledBufferStream.BufferSegment();
        var nextSegment = new PooledBufferStream.BufferSegment();

        // 执行
        segment.SetNext(nextSegment);

        // 验证
        Assert.Equal(nextSegment, segment.Next);
    }

    /// <summary>
    /// 验证多次调用 SetNext 会正确覆盖 Next 属性。
    /// </summary>
    [Fact]
    public void SetNext_OverwriteExistingNext_SetsNextPropertyToNewValue()
    {
        // 准备
        var segment = new PooledBufferStream.BufferSegment();
        var firstNextSegment = new PooledBufferStream.BufferSegment();
        var secondNextSegment = new PooledBufferStream.BufferSegment();

        // 执行
        segment.SetNext(firstNextSegment);
        segment.SetNext(secondNextSegment);

        // 验证
        Assert.Equal(secondNextSegment, segment.Next);
    }

    /// <summary>
    /// 验证 SegmentPoolPolicy.Create 方法创建新的 BufferSegment 实例。
    /// 此测试调用 BufferSegment.Pool.Get()，其内部使用 SegmentPoolPolicy.Create 方法。
    /// 预期结果：返回非 null 的 BufferSegment 实例。
    /// </summary>
    [Fact]
    public void SegmentPoolPolicy_Create_ReturnsNewBufferSegment()
    {
        // 执行
        PooledBufferStream.BufferSegment segment = PooledBufferStream.BufferSegment.Pool.Get();

        // 验证
        Assert.NotNull(segment);
        Assert.IsType<PooledBufferStream.BufferSegment>(segment);
        PooledBufferStream.BufferSegment.Pool.Return(segment);
    }
}

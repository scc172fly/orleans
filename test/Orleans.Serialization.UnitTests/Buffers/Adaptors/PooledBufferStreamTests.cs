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
/// 用于 PooledBufferStream 构建器的单位测试。
/// </summary>
[Trait("Category", "BVT")]
[Trait("Suite", "BVT")]
[Trait("Provider", "None")]
[Trait("Area", "Serialization")]
public class PooledBufferStreamTests
{
    /// <summary>
    /// 验证无参数构建器初始化一个零长度的新实例 
    /// 和适当的流能力。
    /// </summary>
    [Fact]
    public void Constructor_Parameterless_InitializesWithZeroLengthAndValidCapabilities()
    {
        // 排列和法案
        using var stream = new PooledBufferStream();

        // 发货
        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        // 校验默认位置为 0
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 证明带有 minAllocationSize 参数初始化新实例的构建器 
    /// 具有零和适当的流能力。
    /// 测试 minAllocationSize 参数的各种边缘大小写。
    /// </summary>
    /// <param name="minAllocationSize">最低分配规模传递给建筑商。</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public void Constructor_WithMinAllocationSize_InitializesWithZeroLengthAndValidCapabilities(int minAllocationSize)
    {
        // 排列和法案
        using var stream = new PooledBufferStream(minAllocationSize);

        // 发货
        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.True(stream.CanWrite);
        // 校验默认位置为 0
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 证明 PooledBufferStream 构造器初始化长度属性为零,
    /// 不论提供的 minAllocationSize 参数值如何。
    /// 试验案例包括负值、零值、正值、 int.MaxValue值和 int.MinValue 值。
    /// </summary>
    /// <param name="minAllocationSize">启动流所用的最小分配大小 。</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Constructor_WithMinAllocationValues_LengthIsZero(int minAllocationSize)
    {
        // 排列和法案
        using var stream = new PooledBufferStream(minAllocationSize);

        // 发货
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 证明默认 PooledBufferStream 构建器初始化到长度属性为零。
    /// </summary>
    [Fact]
    public void DefaultConstructor_LengthIsZero()
    {
        // 排列和法案
        using var stream = new PooledBufferStream();

        // 发货
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 证明调用租金返回了非null实例 PooledBufferStream ,最初长度为零。
    /// 此测试参数将Rent被调用的次数计算在内, 以确保无论呼号多少都保持行为一致。
    /// </summary>
    /// <param name="rentCount">援用租金的次数。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Rent_MultipleTimes_ReturnsValidInstance(int rentCount)
    {
        // 排列排列
        var instances = new List<PooledBufferStream>();

        // 法案法
        for (int i = 0; i < rentCount; i++)
        {
            var instance = PooledBufferStream.Rent();
            instances.Add(instance);
        }

        // 发货
        Assert.Equal(rentCount, instances.Count);
        foreach (var instance in instances)
        {
            Assert.NotNull(instance);
            // 验证新租的溪流的长度属性为零。
            Assert.Equal(0, instance.Length);
        }
    }

    /// <summary>
    /// 测试返回有效流的回收,确保随后租用的原样与归还的原样相同,从而进行循环利用。
    /// </summary>
    [Fact]
    public void Return_ValidStream_RecyclesObject()
    {
        // 排列排列
        using var stream = PooledBufferStream.Rent();
        // 修改状态以模拟使用 。
        stream.Position = 100;

        // 法案法
        PooledBufferStream.Return(stream);
        var recycledStream = PooledBufferStream.Rent();

        // 发货
        // 期望回收利用同样的例子。
        Assert.True(Object.ReferenceEquals(stream, recycledStream), "The recycled stream instance should be the same as the one returned.");

        // 清理: 再次返回流以避免影响其他测试。
        PooledBufferStream.Return(recycledStream);
    }

    /// <summary>
    /// 测试通过空流返回会引发例外
    /// </summary>
    [Fact]
    public void Return_NullStream_ThrowsException()
    {
        // 安排和法案及裁决
        Assert.Throws<ArgumentNullException>(() => PooledBufferStream.Return(null!));
    }

    /// <summary>
    /// 证明新建的 PooledBufferStream 长度为零,而不论指定的 minAllocationSize。
    /// </summary>
    /// <param name="minAllocationSize">构建流时使用的最低分配大小。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    [InlineData(100)]
    [InlineData(-1)]
    public void Length_OnNewStream_ReturnsZero(int minAllocationSize)
    {
        // 排列排列
        using var stream = new PooledBufferStream(minAllocationSize);

        // 法案法
        long length = stream.Length;

        // 发货
        Assert.Equal(0L, length);
    }

    /// <summary>
    /// 测试 ToArray 在没有将数据写入流时返回空数组。
    /// </summary>
    [Fact]
    public void ToArray_EmptyStream_ReturnsEmptyArray()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 法案法
        byte[] result = stream.ToArray();

        // 发货
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// 证明 CopyTo 方法正确将所有部分写入提供给作者的文字。
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 10, 20, 30 })]
    public void CopyTo_ValidSegments_CopiesDataCorrectly(byte[] expectedData)
    {
        using var stream = new PooledBufferStream();
        stream.Write(expectedData);

        var output = new byte[expectedData.Length];
        // 测试只使用原始缓冲复制, 无法访问序列转换器会话 。
        var writer = Writer.Create(output, session: null!);

        stream.CopyTo(ref writer);

        Assert.Equal(expectedData, output);
    }

    /// <summary>
    /// 证明 CopyTo 方法正确将所有部分写入给提供更大的缓冲字符的作者。
    /// 带有大于5千字节的缓冲测试,以确保多部分功能正常运行。
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
        // 测试只使用原始缓冲复制, 无法访问序列转换器会话 。
        var writer = Writer.Create(output, session: null!);

        stream.CopyTo(ref writer);

        Assert.True(expectedData.SequenceEqual(output));
    }

    /// <summary>
    /// 证明重新设定一个新实例 会导致零长度
    /// </summary>
    [Fact]
    public void Reset_FreshInstance_LengthIsZero()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 法案法
        stream.Reset();

        // 发货
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证在将数据写入流后, 重新设置重置将流长度改成零 。
    /// 使用不同的写数测试多种情景。
    /// </summary>
    /// <param name="writeCount">要写入流的字节数。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(1024)]
    public void Reset_AfterWrite_LengthResetToZero(int writeCount)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        var buffer = new byte[writeCount];
        // 必要时用一些假数据弹出缓冲。
        for (int i = 0; i < writeCount; i++)
        {
            buffer[i] = (byte)(i % 256);
        }

        // 法案法
        stream.Write(buffer, 0, writeCount);
        // 长度视执行情况而定,可能反映也可能不反映书面字节的确切数目。
        // 调用重置应清除任何书面数据并重置长度 。
        stream.Reset();

        // 发货
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 验证调用重置将位置属性重置为零。
    /// 当从池中回收循环流以确保位置时,这一点很重要
    /// 以往使用时不会留下任何痕迹。
    /// </summary>
    /// <param name="initialPosition">调用重置前要设定的初始位置。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1024)]
    public void Reset_WithPosition_ResetsPositionToZero(int initialPosition)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        stream.SetLength(2000); // 长度宽,足以容纳位置
        stream.Position = initialPosition;

        // 法案法
        stream.Reset();

        // 发货
        Assert.Equal(0, stream.Position);
        Assert.Equal(0, stream.Length);
    }

    /// <summary>
    /// 证明 RentReadOnlySequence 返回各种数据长度的正确 ReadOnlySequence 。
    /// 当没有写入数据时,返回空序;否则,该序列与书面数据相符。
    /// </summary>
    /// <param name="dataLength">要写入流的测试数据长度。</param>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(4096)]
    [InlineData(5000)]
    public void RentReadOnlySequence_VariousDataLengths_ReturnsExpectedSequence(int dataLength)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        byte[] expectedData = GenerateTestData(dataLength);
        if (dataLength > 0)
        {
            stream.Write(expectedData, 0, expectedData.Length);
        }

        // 法案法
        ReadOnlySequence<byte> sequence = stream.RentReadOnlySequence();

        // 发货
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
    /// 生成特定长度的确定性测试数据。
    /// </summary>
    /// <param name="length">要生成的数据长度。</param>
    /// <returns>字节数组,填充连续值的元数组 256。</returns>
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
    /// 证明 ReturnReadOnlySequence 在序列启动时不返回 BufferSegment时不执行动作。
    /// 在此测试中, 我们创建一个假 ReadOnlySequenceSegment<byte> 返回非类型对象 BufferSegment。
    /// 预期成果:没有出现任何例外。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void ReturnReadOnlySequence_StartNotBufferSegment_DoesNotThrow(int dummyValue)
    {
        // 排列排列
        var dummySegment = new DummyReadOnlySequenceSegment(dummyValue);
        // 使用假片段创建 ReadOnlySequence 。
        var sequence = new ReadOnlySequence<byte>(dummySegment, 0, dummySegment, dummySegment.Memory.Length);
        using var stream = new PooledBufferStream();

        // 动作和声响( 不应该扔)
        stream.ReturnReadOnlySequence(in sequence);
    }

    /// <summary>
    /// 证明 ReturnReadOnlySequence 返回集合的 BufferSegment 链。
    /// 测试多分层链条中每个段段是否正确返回到池内
    /// 并可用于以后的作业。
    /// </summary>
    [Fact]
    public void ReturnReadOnlySequence_ValidBufferSegmentChain_CallsPoolReturnForEachSegment()
    {
        using var stream = new PooledBufferStream();

        // 写入数据, 以覆盖多个段创建链条
        var dataSize = 10000; // 足够大, 横跨多个区块
        var testData = GenerateTestData(dataSize);
        stream.Write(testData, 0, testData.Length);

        // 租赁 ReadOnlySequence 以获取 BufferSegment 链
        var sequence = stream.RentReadOnlySequence();

        // 核查我们有一个多层链
        Assert.True(sequence.Length > 0);

        // 返回前计算链条中段数
        var segmentCount = CountSegmentsInChain(sequence);
        Assert.True(segmentCount > 1, "Test requires multiple segments to verify chain handling");

        // 取泳池的新鲜片段,在返回前确认它们不同
        var freshSegments = new List<PooledBufferStream.BufferSegment>();
        for (int i = 0; i < segmentCount; i++)
        {
            freshSegments.Add(PooledBufferStream.BufferSegment.Pool.Get());
        }

        // 返回新鲜的片段以避免影响试验
        foreach (var segment in freshSegments)
        {
            PooledBufferStream.BufferSegment.Pool.Return(segment);
        }

        // 返回序列 - 这将返回链条中的所有部分到池
        stream.ReturnReadOnlySequence(sequence);

        // 校验通过获取新区块返回到池中
        // 检查它们是否相同(表明它们是回收的)
        var recycledSegments = new List<PooledBufferStream.BufferSegment>();
        for (int i = 0; i < segmentCount; i++)
        {
            recycledSegments.Add(PooledBufferStream.BufferSegment.Pool.Get());
        }

        // 回收的段段应重新设置,准备再利用
        foreach (var segment in recycledSegments)
        {
            Assert.True(segment.Memory.IsEmpty, "Recycled segment should have empty memory");
            Assert.Equal(0, segment.RunningIndex);
            Assert.Null(segment.Next);
        }

        // 清理,把部件送回池子
        foreach (var segment in recycledSegments)
        {
            PooledBufferStream.BufferSegment.Pool.Return(segment);
        }
    }

    /// <summary>
    /// 计算 ReadOnlySequence 链条中的段数。
    /// </summary>
    /// <param name="sequence">计算段的序列。</param>
    /// <returns>链条中段数。</returns>
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
    /// 模拟实施 ReadOnlySequenceSegment<byte> 模拟一个部分的 GetObject 不返回a BufferSegment.
    /// </summary>
    private class DummyReadOnlySequenceSegment : ReadOnlySequenceSegment<byte>
    {
        private readonly int _dummyValue;

        public DummyReadOnlySequenceSegment(int dummyValue)
        {
            _dummyValue = dummyValue;
            // 以空阵列初始化内存, 用于简单化 。
            Memory = new byte[0];
        }

    }

    /// <summary>
    /// 测试 CanRead 属性返回为真。
    /// 此测试可核实新即时 PooledBufferStream 正确报告可以读取。
    /// </summary>
    [Fact]
    public void CanRead_Property_ReturnsTrue()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 法案法
        bool canRead = stream.CanRead;

        // 发货
        Assert.True(canRead);
    }

    /// <summary>
    /// 测试 CanSeek 属性返回为真。
    /// </summary>
    [Fact]
    public void CanSeek_Property_ReturnsTrue()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 法案法
        bool result = stream.CanSeek;

        // 发货
        Assert.True(result);
    }

    /// <summary>
    /// 测试 CanWrite 属性返回为真,而不论使用的构建器如何。
    /// </summary>
    /// <param name="useNonDefaultConstructor">布尔旗,以测试默认和参数化构造器。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanWrite_Property_ReturnsTrue(bool useNonDefaultConstructor)
    {
        // 排列排列
        using var stream = useNonDefaultConstructor ? new PooledBufferStream(1024) : new PooledBufferStream();

        // 法案法
        bool canWrite = stream.CanWrite;

        // 发货
        Assert.True(canWrite);
    }

    /// <summary>
    /// 测试预设流长的搜索假想 。
    /// 开始: newPosition 等于偏移。
    /// 在当前: newPosition 等于职位加抵消。
    /// 结尾: newPosition 等于长度加抵消。
    /// </summary>
    /// <param name="offset">搜索的偏移参数。</param>
    /// <param name="origin">下 SeekOrigin 搜索中所用的。</param>
    /// <param name="expectedPosition">寻求后预期的新职位。</param>
    [Theory]
    [InlineData(30, SeekOrigin.Begin, 30)]
    [InlineData(20, SeekOrigin.Current, 30)]
    [InlineData(-10, SeekOrigin.End, 90)]
    public void Seek_ValidScenarios_ExpectedPosition(long offset, SeekOrigin origin, long expectedPosition)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        stream.SetLength(100);
        if (origin == SeekOrigin.Current)
        {
            stream.Position = 10;
        }

        // 法案法
        long result = stream.Seek(offset, origin);

        // 发货
        Assert.Equal(expectedPosition, result);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 测试在默认长度( 0) 的溪流上搜索假想的有效 。
    /// 开始和当前,加上0,操作应该成功。
    /// </summary>
    [Theory]
    [InlineData(0, SeekOrigin.Begin, 0)]
    [InlineData(0, SeekOrigin.Current, 0)]
    public void Seek_ZeroOffsetOnEmptyStream_ReturnsZero(long offset, SeekOrigin origin, long expectedPosition)
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 法案法
        long result = stream.Seek(offset, origin);

        // 发货
        Assert.Equal(expectedPosition, result);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 测试 寻找预期抛出 InvalidOperationException的情景 
    /// 当其结果已脱离境界的时候,
    /// </summary>
    /// <param name="offset">搜索的偏移参数。</param>
    /// <param name="origin">下 SeekOrigin 搜索中所用的。</param>
    /// <param name="expectedMessage">预期的例外信息。</param>
    [Theory]
    // 开始:负数抵消。
    [InlineData(-1, SeekOrigin.Begin, "Attempted to seek past beginning of stream")]
    // 对于目前:结果为负。
    [InlineData(-11, SeekOrigin.Current, "Attempted to seek past beginning of stream")]
    // 结尾:负抵消其数量超过长度导致负 newPosition。
    [InlineData(-101, SeekOrigin.End, "Attempted to seek past beginning of stream")]
    // 开始: 抵消大于长度的值。
    [InlineData(101, SeekOrigin.Begin, "Attempted to seek past end of stream")]
    // 现有:结果超过长度。
    [InlineData(91, SeekOrigin.Current, "Attempted to seek past end of stream")]
    // 结尾:正抵消原因 newPosition 超过长度。
    [InlineData(1, SeekOrigin.End, "Attempted to seek past end of stream")]
    public void Seek_OutOfBounds_ThrowsInvalidOperationException(long offset, SeekOrigin origin, string expectedMessage)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        stream.SetLength(100);
        if (origin == SeekOrigin.Current)
        {
            stream.Position = 10;
        }

        // 动作和发售
        var ex = Assert.Throws<InvalidOperationException>(() => stream.Seek(offset, origin));
        Assert.Equal(expectedMessage, ex.Message);
    }

    /// <summary>
    /// 测试无效 SeekOrigin 值导致 ArgumentOutOfRangeException。
    /// </summary>
    [Fact]
    public void Seek_InvalidOrigin_ThrowsArgumentOutOfRangeException()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 动作和发售
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)999));
    }

    /// <summary>
    /// 证明在空流上调用读( 没有写入数据) 返回零 。
    /// </summary>
    [Fact]
    public void Read_WithEmptyStream_ReturnsZero()
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        var buffer = new byte[10];

        // 法案法
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        // 发货
        Assert.Equal(0, bytesRead);
    }

    /// <summary>
    /// 证明调用“阅读”与“无缓冲”相交,会丢出一个 ArgumentNullException。
    /// </summary>
    [Fact]
    public void Read_NullBuffer_ThrowsArgumentNullException()
    {
        // 排列排列
        using var stream = new PooledBufferStream();

        // 动作和发售
        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 5));
    }

    /// <summary>
    /// 证明调用 Read 以无效的偏移或计数会投出 ArgumentOutOfRangeException。
    /// </summary>
    /// <param name="offset">读取时要使用的偏移。</param>
    /// <param name="count">读取时要使用的计数。</param>
    [Theory]
    [InlineData(-1, 5)]
    [InlineData(2, 100)]
    public void Read_InvalidOffsetOrCount_ThrowsArgumentOutOfRangeException(int offset, int count)
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        var buffer = new byte[10];

        // 动作和发售
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, offset, count));
    }

    /// <summary>
    /// 证明调用与当前长度等值的 SetLength 不修改流状态 。
    /// 此测试设定初始非零长度, 然后调用值相同的 SetLength 。
    /// </summary>
    [Fact]
    public void SetLength_ValueEqualsCurrentLength_DoesNotChangeState()
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        stream.Position = 200;
        stream.SetLength(500);
        long originalLength = stream.Length;
        long originalPosition = stream.Position;

        // 法案法
        stream.SetLength(500);

        // 发货
        Assert.Equal(originalLength, stream.Length);
        Assert.Equal(originalPosition, stream.Position);
    }

    /// <summary>
    /// 证明在空或非空流上调用 SetLength 会更新长度并相应调整位置。
    /// 本测试涵盖包括生长和短时在内的各种设想。
    /// </summary>
    /// <param name="initialPosition">流上初始位置设置。</param>
    /// <param name="initialSetLength">第一次 SetLength 呼吁建立一个非空状态。</param>
    /// <param name="newSetLength">第二次 SetLength 呼叫更新长度。</param>
    /// <param name="expectedPosition">更新长度后预期位置。</param>
    /// <param name="expectedLength">更新长度后预期的长度。</param>
    [Theory]
    [InlineData(100, 500, 500, 100, 500)]   // 平等案件,非空白流。
    [InlineData(600, 500, 1000, 500, 1000)]  // 增长情况:最初长度500长,后增长到1000。
    [InlineData(800, 1000, 500, 500, 500)]   // runcate 大小写: 初始长度为 1000 长的短程到 500 。
    public void SetLength_Scenarios_UpdatesLengthAndPosition(int initialPosition, int initialSetLength, int newSetLength, int expectedPosition, int expectedLength)
    {
        // 排列排列
        using var stream = new MemoryStream();
        stream.Position = initialPosition;
        stream.SetLength(initialSetLength);

        // 法案法
        stream.SetLength(newSetLength);

        // 发货
        Assert.Equal(expectedLength, stream.Length);
        Assert.Equal(expectedPosition, stream.Position);
    }

    /// <summary>
    /// 证明在空流上拨打0的 SetLength 时,长度和位置维持在0。
    /// </summary>
    [Fact]
    public void SetLength_ZeroOnEmpty_DoesNotChangeState()
    {
        // 排列排列
        using var stream = new PooledBufferStream();
        stream.Position = 0;

        // 法案法
        stream.SetLength(0);

        // 发货
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Position);
    }

    /// <summary>
    /// 提供无效参数组合用于测试 写方法例外。
    /// 每个对象数组包含:缓冲、偏移、计数和预期例外类型。
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
    /// 提供无效参数时, Wride 将提出适当的例外。
    /// </summary>
    /// <param name="buffer">输入字节数组( 可能是空的) 。</param>
    /// <param name="offset">数组中的起始偏移。</param>
    /// <param name="count">要写入的字节数。</param>
    /// <param name="expectedException">预期的例外类型。</param>
    [Theory]
    [MemberData(nameof(InvalidWriteParameters))]
    public void Write_InvalidParameters_ThrowsException(byte[]? buffer, int offset, int count, Type expectedException)
    {
        using var stream = new PooledBufferStream();
        // Null是用于核实 Stream.Write论证的有意理论输入。
        Assert.Throws(expectedException, () => stream.Write(buffer!, offset, count));
    }

    /// <summary>
    /// 以零计数调用写作测试不会改变流的位置。
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
    /// 测试“正确写入”将数据附加到流中。
    /// 证明书面数据可以通过 ToArray()检索,流的位置适当推进。
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

        // 核实数据是否正确附件
        byte[] writtenData = stream.ToArray();
        Assert.Equal(data, writtenData);

        // 校验流流长度是否与书面数据长度相符
        Assert.Equal(inputLength, stream.Length);

        // 校验位置是否正确推进
        Assert.Equal(initialPosition + inputLength, stream.Position);
    }

    /// <summary>
    /// 证明Flush方法不执行操作,也不提出任何例外。
    /// </summary>
    [Fact]
    public void Flush_NoData_DoesNotThrowException()
    {
        // 排列: 创建一个 PooledBufferStream的新实例。
        using var stream = new PooledBufferStream();

        // 动作和发声: 呼叫 Flush 不应该丢弃例外 。
        var exception = Record.Exception(() => stream.Flush());
        Assert.Null(exception);
    }
}

/// <summary>
/// 单位测试 BufferSegment 类。
/// </summary>
[Trait("Category", "BVT")]
[Trait("Suite", "BVT")]
[Trait("Provider", "None")]
[Trait("Area", "Serialization")]
public class BufferSegmentTests
{
    /// <summary>
    /// 提供初始化方法的测试数据。
    /// 每个测试案例都包含字节数组和相应的 runningIndex 值。
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
    /// 证明初始化方法正确设置了内存和 RunningIndex 属性。
    /// 测试各种边缘的大小写,包括空数组、典型值和边界数值。
    /// </summary>
    /// <param name="inputData">字节数组将设置为内存属性。</param>
    /// <param name="runningIndex">下 runningIndex 待分配值。</param>
    [Theory]
    [MemberData(nameof(GetInitializeTestData))]
    public void Initialize_ValidInput_PropertiesSetCorrectly(byte[] inputData, long runningIndex)
    {
        // 排列排列
        ReadOnlyMemory<byte> readOnlyMemory = new ReadOnlyMemory<byte>(inputData);
        var bufferSegment = new PooledBufferStream.BufferSegment();

        // 法案法
        bufferSegment.Initialize(readOnlyMemory, runningIndex);

        // 发货
        Assert.Equal(readOnlyMemory.ToArray(), bufferSegment.Memory.ToArray());
        Assert.Equal(runningIndex, bufferSegment.RunningIndex);
    }

    /// <summary>
    /// 证明调用 SetNext 并配有有效的 BufferSegment 正确设置了“下一”属性。
    /// </summary>
    [Fact]
    public void SetNext_ValidBufferSegment_SetsNextProperty()
    {
        // 排列排列
        var segment = new PooledBufferStream.BufferSegment();
        var nextSegment = new PooledBufferStream.BufferSegment();

        // 法案法
        segment.SetNext(nextSegment);

        // 发货
        Assert.Equal(nextSegment, segment.Next);
    }

    /// <summary>
    /// 证明调用 SetNext 不止一次正确覆盖了下一项财产。
    /// </summary>
    [Fact]
    public void SetNext_OverwriteExistingNext_SetsNextPropertyToNewValue()
    {
        // 排列排列
        var segment = new PooledBufferStream.BufferSegment();
        var firstNextSegment = new PooledBufferStream.BufferSegment();
        var secondNextSegment = new PooledBufferStream.BufferSegment();

        // 法案法
        segment.SetNext(firstNextSegment);
        segment.SetNext(secondNextSegment);

        // 发货
        Assert.Equal(secondNextSegment, segment.Next);
    }

    /// <summary>
    /// 证明 SegmentPoolPolicy.Create 方法创建了一个新的 BufferSegment 实例。
    /// 这个试调 BufferSegment.Pool.Get() 哪些内部使用 SegmentPoolPolicy.Create 方法。
    /// 预期成绩:返回一个非null BufferSegment 实例。
    /// </summary>
    [Fact]
    public void SegmentPoolPolicy_Create_ReturnsNewBufferSegment()
    {
        // 法案法
        PooledBufferStream.BufferSegment segment = PooledBufferStream.BufferSegment.Pool.Get();

        // 发货
        Assert.NotNull(segment);
        Assert.IsType<PooledBufferStream.BufferSegment>(segment);
        PooledBufferStream.BufferSegment.Pool.Return(segment);
    }
}

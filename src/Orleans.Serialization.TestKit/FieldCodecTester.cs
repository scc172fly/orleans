using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Serialization.Utilities;
using Xunit;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 实地编码测试方法。
    /// </summary>
    [Trait("Category", "BVT")]
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    [ExcludeFromCodeCoverage]
    public abstract class FieldCodecTester<TValue, TCodec> : SerializationTester where TCodec : class, IFieldCodec<TValue>
    {
        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "SerializationTester or its shared fixture owns and disposes the service provider which owns this session pool.")]
        private SerializerSessionPool? _sessionPoolValue;
        private SerializerSessionPool _sessionPool => _sessionPoolValue ??= ServiceProvider.GetRequiredService<SerializerSessionPool>();

        /// <summary>
        /// 初始化 <see cref="FieldCodecTester{TValue, TCodec}"/> 类的新实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 是 <see langword="null"/>.</exception>
        protected FieldCodecTester(ITestOutputHelper output) : base(output ?? throw new ArgumentNullException(nameof(output)))
        {
            output.WriteLine($"Random seed: {RandomSeed}");
        }

        /// <summary>
        /// 初始化 <see cref="FieldCodecTester{TValue, TCodec}"/> 类的新实例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> 或 <paramref name="fixture"/> 是 <see langword="null"/>。</exception>
        protected FieldCodecTester(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output ?? throw new ArgumentNullException(nameof(output)), fixture)
        {
            output.WriteLine($"Random seed: {RandomSeed}");
        }

        /// <inheritdoc/>
        protected override IServiceProvider CreateServiceProvider()
            => CreateServiceProviderCore(Configure);

        internal static IServiceProvider CreateServiceProviderCore(Action<ISerializerBuilder> configure)
        {
            var services = new ServiceCollection();
            _ = services.AddSerializer(builder => builder.Configure(config => config.AddFieldCodec(typeof(TCodec))));

            if (!typeof(TCodec).IsAbstract && !typeof(TCodec).IsInterface)
            {
                _ = services.AddSingleton<TCodec>();
            }

            _ = services.AddSerializer(configure);

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// 拿到会话池
        /// </summary>
        protected SerializerSessionPool SessionPool => _sessionPool;

        /// <summary>
        /// 获得最大段数用于缓冲测试。
        /// </summary>
        protected virtual int[] MaxSegmentSizes => [16];

        /// <summary>
        /// 配置序列器。
        /// </summary>
        protected virtual void Configure(ISerializerBuilder builder)
        {
        }

        /// <summary>
        /// 创建编码器。
        /// </summary>
        protected virtual TCodec CreateCodec() => ServiceProvider.GetRequiredService<TCodec>();

        /// <summary>
        /// 创建值。
        /// </summary>
        protected abstract TValue CreateValue();

        /// <summary>
        /// 获得测试值。
        /// </summary>
        protected abstract TValue[] TestValues { get; }

        /// <summary>
        /// 将平等的两个价值观进行比较。
        /// </summary>
        protected virtual bool Equals(TValue? left, TValue? right) => EqualityComparer<TValue>.Default.Equals(left!, right!);

        /// <summary>
        /// 获得一个价值提供方代表。
        /// </summary>
        protected virtual Action<Action<TValue>>? ValueProvider { get; }

        /// <summary>
        /// 创建在序列测试期间要写入的值实例。
        /// </summary>
        /// <param name="input">试验提供的价值。</param>
        /// <returns>要写入的值实例。</returns>
        [return: NotNullIfNotNull(nameof(input))]
        protected virtual TValue? GetWriteCopy(TValue? input) => input;

        /// <summary>
        /// 检查编码器在写入流和从流读时是否正确预发引用计数。
        /// </summary>
        [Fact]
        public void CorrectlyAdvancesReferenceCounterStream()
        {
            var stream = new MemoryStream();
            using var writerSession = _sessionPool.GetSession();
            using var readerSession = _sessionPool.GetSession();
            var writer = Writer.Create(stream, writerSession);
            var writerCodec = CreateCodec();

            // 写入字段。 这应该涉及在会话中至少标记一个引用。
            Assert.Equal(0, writer.Position);

            foreach (var value in TestValues)
            {
                var beforeReference = writer.Session.ReferencedObjects.CurrentReferenceId;
                writerCodec.WriteField(ref writer, 0, typeof(TValue), value);
                Assert.True(writer.Position > 0);

                writer.Commit();
                var afterReference = writer.Session.ReferencedObjects.CurrentReferenceId;
                Assert.True(beforeReference < afterReference, $"Writing a field should result in at least one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
                if (value is null)
                {
                    Assert.True(beforeReference + 1 == afterReference, $"Writing a null field should result in exactly one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
                }

                stream.Flush();

                stream.Position = 0;
                var reader = Reader.Create(stream, readerSession);

                var previousPos = reader.Position;
                Assert.Equal(0, previousPos);
                var readerCodec = CreateCodec();
                var readField = reader.ReadFieldHeader();

                Assert.True(reader.Position > previousPos);
                previousPos = reader.Position;

                beforeReference = reader.Session.ReferencedObjects.CurrentReferenceId;
                var readValue = readerCodec.ReadValue(ref reader, readField);

                Assert.True(reader.Position > previousPos);

                afterReference = reader.Session.ReferencedObjects.CurrentReferenceId;
                Assert.True(beforeReference < afterReference, $"Reading a field should result in at least one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
                if (readValue is null)
                {
                    Assert.True(beforeReference + 1 == afterReference, $"Reading a null field should result in at exactly one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
                }
            }
        }

        /// <summary>
        /// 检查编码器在写入管道和读取管道时是否正确提前了引用柜台。
        /// </summary>
        [Fact]
        public void CorrectlyAdvancesReferenceCounter()
        {
            var pipe = new Pipe();
            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.Create(pipe.Writer, writerSession);
            var writerCodec = CreateCodec();
            var beforeReference = writer.Session.ReferencedObjects.CurrentReferenceId;

            // 写入字段。 这应该涉及在会话中至少标记一个引用。
            Assert.Equal(0, writer.Position);

            writerCodec.WriteField(ref writer, 0, typeof(TValue), CreateValue());
            Assert.True(writer.Position > 0);

            writer.Commit();
            var afterReference = writer.Session.ReferencedObjects.CurrentReferenceId;
            Assert.True(beforeReference < afterReference, $"Writing a field should result in at least one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = _sessionPool.GetSession();
            var reader = Reader.Create(readResult.Buffer, readerSession);

            var previousPos = reader.Position;
            Assert.Equal(0, previousPos);
            var readerCodec = CreateCodec();
            var readField = reader.ReadFieldHeader();

            Assert.True(reader.Position > previousPos);
            previousPos = reader.Position;

            beforeReference = reader.Session.ReferencedObjects.CurrentReferenceId;
            _ = readerCodec.ReadValue(ref reader, readField);

            Assert.True(reader.Position > previousPos);

            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();
            afterReference = reader.Session.ReferencedObjects.CurrentReferenceId;
            Assert.True(beforeReference < afterReference, $"Reading a field should result in at least one reference being marked in the session. Before: {beforeReference}, After: {afterReference}");
        }

        /// <summary>
        /// 检查代码器使用集合流时圆轨值是否正确。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_StreamPooled()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var toWrite = GetWriteCopy(original);
                var buffer = new MemoryStream();

                var writer = Writer.CreatePooled(buffer, _sessionPool.GetSession());
                serializer.Serialize(toWrite, ref writer);
                buffer.Flush();
                writer.Dispose();

                buffer.Position = 0;
                var reader = Reader.Create(buffer, _sessionPool.GetSession());
                var deserialized = serializer.Deserialize(ref reader)!;
                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            }
        }

        /// <summary>
        /// 检查在写入一个空格时,编码器圆脚值是否正确。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_Span()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var buffer = new byte[8096].AsSpan();
                var toWrite = GetWriteCopy(original);

                var writer = Writer.Create(buffer, _sessionPool.GetSession());
                serializer.Serialize(toWrite, ref writer);

                var reader = Reader.Create(buffer, _sessionPool.GetSession());
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            }
        }

        /// <summary>
        /// 检查在写入数组时,编码器圆轨值是否正确。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_Array()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var buffer = new byte[8096];
                var toWrite = GetWriteCopy(original);

                var writer = Writer.Create(buffer, _sessionPool.GetSession());
                serializer.Serialize(toWrite, ref writer);

                var reader = Reader.Create(buffer, _sessionPool.GetSession());
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            }
        }

        /// <summary>
        /// 检查编码器在写入内存切块时是否正确值。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_Memory()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var buffer = (new byte[8096]).AsMemory();
                var toWrite = GetWriteCopy(original);

                var writer = Writer.Create(buffer, _sessionPool.GetSession());
                serializer.Serialize(toWrite, ref writer);

                var reader = Reader.Create(buffer, _sessionPool.GetSession());
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            }
        }

        /// <summary>
        /// 检查编码器在写入内存流时是否正确值。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_MemoryStream()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var toWrite = GetWriteCopy(original);
                var buffer = new MemoryStream();
                using var writerSession = _sessionPool.GetSession();
                var writer = Writer.Create(buffer, writerSession);
                serializer.Serialize(toWrite, ref writer);
                writer.Commit();
                buffer.Flush();
                buffer.SetLength(buffer.Position);

                buffer.Position = 0;
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(buffer, readerSession);
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                Assert.Equal(writer.Position, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }
        }

        /// <summary>
        /// 检查读取字节byte、 模拟碎裂读时代码集是否正确圆曲值 。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer_ReadByteByByte()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var toWrite = GetWriteCopy(original);
                var buffer = new TestMultiSegmentBufferWriter(maxAllocationSize: 1024);
                using var writerSession = _sessionPool.GetSession();
                var writer = Writer.Create(buffer, writerSession);
                serializer.Serialize(toWrite, ref writer);
                writer.Commit();
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(buffer.GetReadOnlySequence(maxSegmentSize: 1), readerSession);
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                Assert.Equal(writer.Position, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }
        }

        /// <summary>
        /// 检查编码器是否产生有效的位流。
        /// </summary>
        [Fact]
        public void ProducesValidBitStream()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();
            foreach (var value in TestValues)
            {
                Test(value);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue value)
            {
                var array = serializer.SerializeToArray(value);
                var session = _sessionPool.GetSession();
                var reader = Reader.Create(array, session);
                var formatted = new StringBuilder();
                try
                {
                    BitStreamFormatter.Format(ref reader, formatted);
                    Assert.True(formatted.ToString() is string { Length: > 0 });
                }
                catch (Exception exception)
                {
                    Assert.True(false, $"Formatting failed with exception: {exception} and partial result: \"{formatted}\"");
                }
            }
        }

        /// <summary>
        /// 检查各种缓冲作者是否产生比特角度相同的结果 。
        /// </summary>
        [Fact]
        public void WritersProduceSameResults()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                byte[] expected;

                {
                    var buffer = new TestMultiSegmentBufferWriter(1024);
                    var writer = Writer.Create(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    expected = buffer.GetReadOnlySequence(0).ToArray();
                }

                {
                    var buffer = new MemoryStream();
                    var writer = Writer.Create(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    buffer.Flush();
                    buffer.SetLength(buffer.Position);
                    buffer.Position = 0;
                    var result = buffer.ToArray();
                    Assert.Equal(expected, result);
                }

                {
                    var writer = Writer.CreatePooled(_sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    var result = writer.Output.ToArray();
                    Assert.Equal(expected, result);
                }

                var bytes = new byte[10240];

                {
                    var buffer = bytes.AsMemory();
                    var writer = Writer.Create(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    var result = buffer[..writer.Output.BytesWritten].ToArray();
                    Assert.Equal(expected, result);
                }

                bytes.AsSpan().Clear();

                {
                    var buffer = bytes.AsSpan();
                    var writer = Writer.Create(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    var result = buffer[..writer.Output.BytesWritten].ToArray();
                    Assert.Equal(expected, result);
                }

                bytes.AsSpan().Clear();

                {
                    var buffer = bytes;
                    var writer = Writer.Create(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    var result = buffer.AsSpan(0, writer.Output.BytesWritten).ToArray();
                    Assert.Equal(expected, result);
                }

                bytes.AsSpan().Clear();

                {
                    var buffer = new MemoryStream(bytes);
                    var writer = Writer.CreatePooled(buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    buffer.Flush();
                    buffer.SetLength(buffer.Position);
                    buffer.Position = 0;
                    var result = buffer.ToArray();
                    writer.Dispose();
                    Assert.Equal(expected, result);
                }

                bytes.AsSpan().Clear();

                {
                    var buffer = new MemoryStream(bytes);
                    var writer = Writer.Create((Stream)buffer, _sessionPool.GetSession());
                    serializer.Serialize(GetWriteCopy(original), ref writer);
                    buffer.Flush();
                    buffer.SetLength(buffer.Position);
                    buffer.Position = 0;
                    var result = buffer.ToArray();
                    Assert.Equal(expected, result);
                }
            }
        }

        /// <summary>
        /// 检查是否可圆盘地打印一个强打的数值集合。
        /// </summary>
        [Fact]
        public void CanRoundTripCollectionViaSerializer()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<List<TValue>>>();

            var original = new List<TValue>();
            var originalCopy = new List<TValue>();
            original.AddRange(TestValues);
            foreach (var value in original)
            {
                originalCopy.Add(GetWriteCopy(value)!);
            }

            for (var i = 0; i < 5; i++)
            {
                var o = CreateValue();
                var c = GetWriteCopy(o);
                original.Add(o);
                originalCopy.Add(c!);
            }

            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(writerSession);
            serializer.Serialize(originalCopy, ref writer);
            using var readerSession = _sessionPool.GetSession();
            var reader = Reader.Create(writer.Output, readerSession);
            var deserialized = serializer.Deserialize(ref reader)!;

            Assert.Equal(original.Count, deserialized.Count);
            for (var i = 0; i < original.Count; ++i)
            {
                var isEqual = Equals(original[i], deserialized[i]);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value at index {i}, \"{deserialized[i]}\", must equal original value, \"{original[i]}\"");
            }

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
        }

        /// <summary>
        /// 检查是否可圆盘地打印一个强打的数值集合。
        /// </summary>
        [Fact]
        public void CanRoundTripWeaklyTypedCollectionViaSerializer()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<List<object?>>>();

            var original = new List<object?>();
            var originalCopy = new List<object?>();
            foreach (var value in TestValues)
            {
                var o = value;
                var c = GetWriteCopy(o);
                original.Add(o);
                originalCopy.Add(c);
            }

            for (var i = 0; i < 5; i++)
            {
                var o = CreateValue();
                var c = GetWriteCopy(o);
                original.Add(o);
                originalCopy.Add(c);
            }

            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(writerSession);
            serializer.Serialize(originalCopy, ref writer);
            using var readerSession = _sessionPool.GetSession();
            var reader = Reader.Create(writer.Output, readerSession);
            var deserialized = serializer.Deserialize(ref reader)!;

            Assert.Equal(original.Count, deserialized.Count);
            for (var i = 0; i < original.Count; ++i)
            {
                var left = (TValue?)original[i];
                var right = (TValue?)deserialized[i];
                var isEqual = Equals(left, right);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value at index {i}, \"{right}\", must equal original value, \"{left}\"");
            }

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
        }

        /// <summary>
        /// 检查是否在使用一个图普时可以将值作为字段进行圆边切换。
        /// </summary>
        [Fact]
        public void CanRoundTripTupleViaSerializer()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<(string, TValue?, TValue?, string)>>();

            var original = (Guid.NewGuid().ToString(), CreateValue(), CreateValue(), Guid.NewGuid().ToString());
            var originalCopy = (original.Item1, GetWriteCopy(original.Item2), GetWriteCopy(original.Item3), original.Item4);

            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.CreatePooled(writerSession);
            serializer.Serialize(originalCopy, ref writer);
            using var readerSession = _sessionPool.GetSession();
            var reader = Reader.Create(writer.Output, readerSession);
            var deserialized = serializer.Deserialize(ref reader);

            var isEqual = Equals(original.Item1, deserialized.Item1);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value for item 1, \"{deserialized.Item1}\", must equal original value, \"{original.Item1}\"");
            isEqual = Equals(original.Item2, deserialized.Item2);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value for item 2, \"{deserialized.Item2}\", must equal original value, \"{original.Item2}\"");
            isEqual = Equals(original.Item3, deserialized.Item3);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value for item 3, \"{deserialized.Item3}\", must equal original value, \"{original.Item3}\"");
            isEqual = Equals(original.Item4, deserialized.Item4);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value for item 4, \"{deserialized.Item4}\", must equal original value, \"{original.Item4}\"");

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
        }

        /// <summary>
        /// 检查是否可以将值进行圆倍的切换 <see cref="Serializer{T}"/> 使用时 <typeparamref name="TValue"/> 用作类型参数。
        /// </summary>
        [Fact]
        public void CanRoundTripViaSerializer()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<TValue>>();

            foreach (var original in TestValues)
            {
                Test(original);
            }

            if (ValueProvider is { } valueProvider)
            {
                valueProvider(Test);
            }

            void Test(TValue original)
            {
                var toWrite = GetWriteCopy(original);
                var buffer = new TestMultiSegmentBufferWriter(1024);
                using var writerSession = _sessionPool.GetSession();
                var writer = Writer.Create(buffer, writerSession);
                serializer.Serialize(toWrite, ref writer);
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(buffer.GetReadOnlySequence(0), readerSession);
                var deserialized = serializer.Deserialize(ref reader);

                var isEqual = Equals(original, deserialized);
                Assert.True(
                    isEqual,
                    isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                Assert.Equal(writer.Position, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }
        }

        /// <summary>
        /// 检查是否可以将值进行圆倍的切换 <see cref="Serializer{T}"/> 使用时 <see cref="object"/> 用作类型参数。
        /// </summary>
        [Fact]
        public void CanRoundTripViaObjectSerializer()
        {
            var serializer = ServiceProvider.GetRequiredService<Serializer<object?>>();

            var buffer = new byte[10240];

            foreach (var original in TestValues)
            {
                buffer.AsSpan().Clear();
                var toWrite = GetWriteCopy(original);
                using var writerSession = _sessionPool.GetSession();
                var writer = Writer.Create(buffer, writerSession);
                serializer.Serialize(toWrite, ref writer);

                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(buffer, readerSession);
                var deserializedObject = serializer.Deserialize(ref reader);
                if (original != null && !typeof(TValue).IsEnum)
                {
                    var deserialized = Assert.IsAssignableFrom<TValue>(deserializedObject);
                    var isEqual = Equals(original, deserialized);
                    Assert.True(
                        isEqual,
                        isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                }
                else if (typeof(TValue).IsEnum)
                {
                    var deserialized = (TValue)deserializedObject!;
                    var isEqual = Equals(original, deserialized);
                    Assert.True(
                        isEqual,
                        isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                }
                else
                {
                    Assert.Null(deserializedObject);
                }

                Assert.Equal(writer.Position, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }
        }

        /// <summary>
        /// 检查圆倾角值是否相等。
        /// </summary>
        [Fact]
        public void RoundTrippedValuesEqual() => TestRoundTrippedValue(CreateValue());

        /// <summary>
        /// 检查圆角的默认值是否等于。
        /// </summary>
        [Fact]
        public void CanRoundTripDefaultValueViaCodec() => TestRoundTrippedValue(default);

        /// <summary>
        /// 检查值是否可以跳过。
        /// </summary>
        [Fact]
        public void CanSkipValue() => CanBeSkipped(default);

        /// <summary>
        /// 检查默认值是否可以跳过。
        /// </summary>
        [Fact]
        public void CanSkipDefaultValue() => CanBeSkipped(default);

        /// <summary>
        /// 检查缓冲带是否被正确处理 。
        /// </summary>
        [Fact]
        public void CorrectlyHandlesBuffers()
        {
            var testers = BufferTestHelper<TValue>.GetTestSerializers(ServiceProvider, MaxSegmentSizes);

            foreach (var tester in testers)
            {
                foreach (var maxSegmentSize in MaxSegmentSizes)
                {
                    foreach (var value in TestValues)
                    {
                        var toWrite = GetWriteCopy(value);
                        var buffer = tester.Serialize(toWrite);
                        var sequence = buffer.GetReadOnlySequence(maxSegmentSize);
                        tester.Deserialize(sequence, out var output);
                        var bufferWriterType = tester.GetType().BaseType?.GenericTypeArguments[1];
                        var isEqual = Equals(value, output);
                        Assert.True(isEqual,
                            isEqual ? string.Empty : $"Deserialized value {output} must be equal to serialized value {value}. " +
                            $"IBufferWriter<> type: {bufferWriterType}, Max Read Segment Size: {maxSegmentSize}. " +
                            $"Buffer: 0x{string.Join(" ", sequence.ToArray().Select(b => $"{b:X2}"))}");
                    }
                }
            }
        }

        private void CanBeSkipped(TValue? original)
        {
            var pipe = new Pipe();
            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.Create(pipe.Writer, writerSession);
            var writerCodec = CreateCodec();
            var toWrite = GetWriteCopy(original);
            writerCodec.WriteField(ref writer, 0, typeof(TValue), toWrite);
            var expectedLength = writer.Position;
            writer.Commit();
            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);

            {
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var readField = reader.ReadFieldHeader();
                reader.SkipField(readField);
                Assert.Equal(expectedLength, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }

            {
                var codec = new SkipFieldCodec();
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var readField = reader.ReadFieldHeader();
                var shouldBeNull = codec.ReadValue(ref reader, readField);
                Assert.Null(shouldBeNull);
                Assert.Equal(expectedLength, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }

            {
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var readField = reader.ReadFieldHeader();
                reader.ConsumeUnknownField(readField);
                Assert.Equal(expectedLength, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();
        }

        private void TestRoundTrippedValue(TValue? original)
        {
            var pipe = new Pipe();
            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.Create(pipe.Writer, writerSession);
            var writerCodec = CreateCodec();
            writerCodec.WriteField(ref writer, 0, typeof(TValue), GetWriteCopy(original));
            writer.Commit();
            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = _sessionPool.GetSession();
            var reader = Reader.Create(readResult.Buffer, readerSession);
            var readerCodec = CreateCodec();
            var readField = reader.ReadFieldHeader();
            var deserialized = readerCodec.ReadValue(ref reader, readField);
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();
            var isEqual = Equals(original, deserialized);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
        }

        /// <summary>
        /// 通过编码器的圆形跟踪值。
        /// </summary>
        [return: MaybeNull]
        protected T RoundTripThroughCodec<T>([AllowNull] T original)
        {
            T? result;
            using (var readerSession = SessionPool.GetSession())
            using (var writeSession = SessionPool.GetSession())
            {
                var writer = Writer.CreatePooled(writeSession);
                try
                {
                    var codec = ServiceProvider.GetRequiredService<ICodecProvider>().GetCodec<T>();
                    codec.WriteField(
                        ref writer,
                        0,
                        null,
                        original);
                    writer.Commit();

                    var output = writer.Output.AsReadOnlySequence();
                    var reader = Reader.Create(output, readerSession);

                    var previousPos = reader.Position;
                    var initialHeader = reader.ReadFieldHeader();
                    Assert.True(reader.Position > previousPos);

                    result = codec.ReadValue(ref reader, initialHeader);
                }
                finally
                {
                    writer.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// 通过未类型序列器回转一个值。
        /// </summary>
        [return: NotNullIfNotNull(nameof(original))]
        protected object? RoundTripThroughUntypedSerializer(object? original, out string formattedBitStream)
        {
            object? result;
            using var readerSession = SessionPool.GetSession();
            using var writeSession = SessionPool.GetSession();

            using var bufferWriter = new ArcBufferWriter();
            var writer = Writer.Create(bufferWriter, writeSession);
            try
            {
                var serializer = ServiceProvider.GetService<Serializer<object>>()!;
                serializer.Serialize(original, ref writer);

                using var analyzerSession = SessionPool.GetSession();
                using var output = bufferWriter.ConsumeSlice(bufferWriter.Length);
                var analyzerReader = Reader.Create(output, analyzerSession);
                formattedBitStream = BitStreamFormatter.Format(ref analyzerReader);

                var reader = Reader.Create(output, readerSession);

                result = serializer.Deserialize(ref reader);
            }
            finally
            {
                writer.Dispose();
            }

            return result;
        }
    }

}

using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Xunit;
using System.Threading.Tasks;

namespace Orleans.Serialization.UnitTests
{
    /// <summary>
    /// Orleans 序列化版本容错能力测试。
    /// 
    /// 版本容错是 Orleans 的关键功能，支持：
    /// - 为类型添加新字段，而不破坏现有序列化数据
    /// - 平滑移除字段（仍可反序列化含有已移除字段的旧数据）
    /// - 在保持兼容性的前提下修改类型层次结构
    /// - 在分布式系统中进行无停机滚动升级
    /// 
    /// Orleans 通过以下方式实现版本容错：
    /// - 基于字段的序列化（不依赖字段顺序）
    /// - 在不同版本间保持稳定的显式字段 ID
    /// - 反序列化时妥善处理未知字段
    /// - 支持架构演进模式
    /// </summary>
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    public class ManualVersionToleranceTests
    {
        private const string TestString = "hello, Orleans.Serialization";
        private readonly ITestOutputHelper _log;
        private readonly IServiceProvider _serviceProvider;
        private readonly CodecProvider _codecProvider;
        private readonly IFieldCodec<SubType> _serializer;
        private readonly ServiceCollection _serviceCollection;

        public ManualVersionToleranceTests(ITestOutputHelper log)
        {
            _log = log;
            var serviceCollection = new ServiceCollection();
            _serviceCollection = serviceCollection;
            _ = _serviceCollection.AddSerializer(builder =>
              {
                  _ = builder.Configure(configuration =>
                    {
                        configuration.AddSerializer(typeof(SubTypeSerializer));
                        configuration.AddSerializer(typeof(BaseTypeSerializer));
                        configuration.AddSerializer(typeof(ObjectWithNewFieldTypeSerializer));
                        configuration.AddSerializer(typeof(ObjectWithoutNewFieldTypeSerializer));

                        // 有意移除这些类型的生成式序列化器。测试期间将重新添加。
                        configuration.Serializers.RemoveWhere(s => typeof(IFieldCodec<ObjectWithNewField>).IsAssignableFrom(s));
                        configuration.Serializers.RemoveWhere(s => typeof(IFieldCodec<ObjectWithoutNewField>).IsAssignableFrom(s));
                    });
              });

            _serviceProvider = _serviceCollection.BuildServiceProvider();

            _codecProvider = _serviceProvider.GetRequiredService<CodecProvider>();
            _serializer = _codecProvider.GetCodec<SubType>();
        }

        [Fact]
        public void VersionTolerance_RoundTrip_Tests()
        {
            RoundTripTest(
                new SubType
                {
                    BaseTypeString = "HOHOHO",
                    AddedLaterString = TestString,
                    String = null,
                    Int = 1,
                    Ref = TestString
                });

            RoundTripTest(
                new SubType
                {
                    BaseTypeString = "base",
                    String = "sub",
                    Int = 2,
                });

            RoundTripTest(
                new SubType
                {
                    BaseTypeString = "base",
                    String = "sub",
                    Int = int.MinValue,
                });

            RoundTripTest(
                new SubType
                {
                    BaseTypeString = TestString,
                    String = TestString,
                    Int = 10
                });

            RoundTripTest(
                new SubType
                {
                    BaseTypeString = TestString,
                    String = null,
                    Int = 1
                });

            RoundTripTest(
                new SubType
                {
                    BaseTypeString = TestString,
                    String = null,
                    Int = 1
                });

            TestSkip(
                new SubType
                {
                    BaseTypeString = TestString,
                    String = null,
                    Int = 1
                });

            var self = new SubType
            {
                BaseTypeString = "HOHOHO",
                AddedLaterString = TestString,
                String = null,
                Int = 1
            };
            self.Ref = self;
            RoundTripTest(self, assertRef: false);

            self.Ref = Guid.NewGuid();
            RoundTripTest(self, assertRef: false);
        }

        private SerializerSession GetSession() => _serviceProvider.GetRequiredService<SerializerSessionPool>().GetSession();

        private void RoundTripTest(SubType expected, bool assertRef = true)
        {
            using var writerSession = GetSession();
            var pipe = new Pipe();
            var writer = Writer.Create(pipe.Writer, writerSession);

            _serializer.WriteField(ref writer, 0, typeof(SubType), expected);
            writer.Commit();

            _log.WriteLine($"Size: {writer.Position} bytes.");
            _log.WriteLine($"Wrote References:\n{GetWriteReferenceTable(writerSession)}");

            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();
            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSesssion = GetSession();
            var reader = Reader.Create(readResult.Buffer, readerSesssion);
            var initialHeader = reader.ReadFieldHeader();

            _log.WriteLine("Header:");
            _log.WriteLine(initialHeader.ToString());

            var actual = _serializer.ReadValue(ref reader, initialHeader)!;
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();

            _log.WriteLine($"Expect: {expected}\nActual: {actual}");

            Assert.Equal(expected.BaseTypeString, actual.BaseTypeString);
            Assert.Null(actual.AddedLaterString); // 反序列化器不“知晓”这个后来添加的字段，这体现了版本容错能力。
            Assert.Equal(expected.String, actual.String);
            Assert.Equal(expected.Int, actual.Int);
            if (assertRef)
            {
                Assert.Equal(expected.Ref, actual.Ref);
            }
            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(writer.Session.ReferencedObjects.CurrentReferenceId, reader.Session.ReferencedObjects.CurrentReferenceId);

            var references = GetReadReferenceTable(reader.Session);
            _log.WriteLine($"Read references:\n{references}");
        }

        private void TestSkip(SubType expected)
        {
            using var writerSession = GetSession();
            var pipe = new Pipe();
            var writer = Writer.Create(pipe.Writer, writerSession);

            _serializer.WriteField(ref writer, 0, typeof(SubType), expected);
            writer.Commit();

            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            var objectWithNewFieldSerializer = _codecProvider.GetCodec<ObjectWithNewField>();
            var objectWithoutNewFieldSerializer = _codecProvider.GetCodec<ObjectWithoutNewField>();
            pipe.Writer.Complete();
            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = GetSession();
            var reader = Reader.Create(readResult.Buffer, readerSession);
            var initialHeader = reader.ReadFieldHeader();
            var skipCodec = new SkipFieldCodec();
            _ = skipCodec.ReadValue(ref reader, initialHeader);
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();
            Assert.Equal(writer.Session.ReferencedObjects.CurrentReferenceId, reader.Session.ReferencedObjects.CurrentReferenceId);
            _log.WriteLine($"Skipped {reader.Position} bytes.");
        }

        private static StringBuilder GetReadReferenceTable(SerializerSession session)
        {
            var table = session.ReferencedObjects.CopyReferenceTable();
            var references = new StringBuilder();
            foreach (var entry in table)
            {
                _ = references.AppendLine($"\t[{entry.Key}] {entry.Value}");
            }
            return references;
        }

        private static StringBuilder GetWriteReferenceTable(SerializerSession session)
        {
            var table = session.ReferencedObjects.CopyIdTable();
            var references = new StringBuilder();
            foreach (var entry in table)
            {
                _ = references.AppendLine($"\t[{entry.Value}] {entry.Key}");
            }
            return references;
        }

        [Fact]
        public async Task ObjectWithNewFieldTest()
        {
            var expected = new ObjectWithNewField("blah", newField: "this field will not be manually serialized -- the binary will not have it!");
            var pipe = new Pipe();

            // 使用忽略 ObjectWithNewField.NewField 的手写序列化器。
            // 不序列化 NewField，以模拟由该对象的旧版本生成的二进制数据。
            var objectWithNewFieldSerializer = _codecProvider.GetCodec<ObjectWithNewField>();
            var objectWithoutNewFieldSerializer = _codecProvider.GetCodec<ObjectWithoutNewField>();
            _ = Assert.IsType<ConcreteTypeSerializer<ObjectWithNewField, ObjectWithNewFieldTypeSerializer>>(objectWithNewFieldSerializer);

            var (writerPosition, writerCurrentReferenceId) = WriteToPipe();
            (int WriterPosition, uint CurrentReferenceId) WriteToPipe()
            {
                using var writerSession = GetSession();
                var writer = Writer.Create(pipe.Writer, writerSession);
                objectWithNewFieldSerializer.WriteField(ref writer, 0, typeof(ObjectWithNewField), expected);
                writer.Commit();

                _log.WriteLine($"Size: {writer.Position} bytes.");
                _log.WriteLine($"Wrote References:\n{GetWriteReferenceTable(writerSession)}");
                return (writer.Position, writer.Session.ReferencedObjects.CurrentReferenceId);
            }

            _ = await pipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = GetSession();
            var (actual, readerPosition, readerCurrentReferenceId) = ReadValue();
            (ObjectWithNewField Value, long ReaderPosition, uint CurrentReferenceId) ReadValue()
            {
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var initialHeader = reader.ReadFieldHeader();

                _log.WriteLine("Header:");
                _log.WriteLine(initialHeader.ToString());

                GetGeneratedSerializer(out objectWithNewFieldSerializer);
                Assert.IsNotType<ConcreteTypeSerializer<ObjectWithNewField, ObjectWithNewFieldTypeSerializer>>(objectWithNewFieldSerializer);

                // 使用能够反序列化 NewField 的生成式反序列化器。
                var value = objectWithNewFieldSerializer.ReadValue(ref reader, initialHeader)!;
                return (value, reader.Position, reader.Session.ReferencedObjects.CurrentReferenceId);
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();

            _log.WriteLine($"Expect: {expected}\nActual: {actual}");

            Assert.Equal(expected.Blah, actual.Blah);
            objectWithNewFieldSerializer = _codecProvider.GetCodec<ObjectWithNewField>();
            objectWithoutNewFieldSerializer = _codecProvider.GetCodec<ObjectWithoutNewField>();
            Assert.Null(actual.NewField); // null，因为二进制数据中不应包含该字段。
            Assert.Equal(expected.Version, actual.Version);
            Assert.Equal(writerPosition, readerPosition);
            Assert.Equal(writerCurrentReferenceId, readerCurrentReferenceId);

            var references = GetReadReferenceTable(readerSession);
            _log.WriteLine($"Read references:\n{references}");
        }

        [Fact]
        public async Task ObjectWithoutNewFieldTest()
        {
            var expected = new ObjectWithoutNewField("blah");

            using var writerSession = GetSession();
            var pipe = new Pipe();

            var objectWithNewFieldSerializer = _codecProvider.GetCodec<ObjectWithNewField>();
            var objectWithoutNewFieldSerializer = _codecProvider.GetCodec<ObjectWithoutNewField>();
            // 使用会写入新字段的手写序列化器。
            // 序列化新字段，以模拟由该对象的新版本生成的二进制数据。
            _ = Assert.IsType<ConcreteTypeSerializer<ObjectWithoutNewField, ObjectWithoutNewFieldTypeSerializer>>(objectWithoutNewFieldSerializer);

            var (writerPosition, writerCurrentReferenceId) = WriteToPipe();
            (int WriterPosition, uint CurrentReferenceId) WriteToPipe()
            {
                var writer = Writer.Create(pipe.Writer, writerSession);
                objectWithoutNewFieldSerializer.WriteField(ref writer, 0, typeof(ObjectWithoutNewField), expected);
                writer.Commit();

                _log.WriteLine($"Size: {writer.Position} bytes.");
                _log.WriteLine($"Wrote References:\n{GetWriteReferenceTable(writerSession)}");
                return (writer.Position, writer.Session.ReferencedObjects.CurrentReferenceId);
            }

            _ = await pipe.Writer.FlushAsync(TestContext.Current.CancellationToken);
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = GetSession();

            var (actual, readerPosition, readerCurrentReferenceId) = ReadValue();
            (ObjectWithoutNewField Value, long ReaderPosition, uint CurrentReferenceId) ReadValue()
            {
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var initialHeader = reader.ReadFieldHeader();

                _log.WriteLine("Header:");
                _log.WriteLine(initialHeader.ToString());

                GetGeneratedSerializer(out objectWithoutNewFieldSerializer);
                Assert.IsNotType<ConcreteTypeSerializer<ObjectWithoutNewField, ObjectWithoutNewFieldTypeSerializer>>(objectWithoutNewFieldSerializer);

                // 使用无法反序列化该已序列化新字段的生成式反序列化器。
                var value = objectWithoutNewFieldSerializer.ReadValue(ref reader, initialHeader)!;
                return (value, reader.Position, reader.Session.ReferencedObjects.CurrentReferenceId);
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();

            _log.WriteLine($"Expect: {expected}\nActual: {actual}");

            Assert.Equal(expected.Blah, actual.Blah);
            Assert.Equal(expected.Version, actual.Version);
            Assert.Equal(writerPosition, readerPosition);
            Assert.Equal(writerCurrentReferenceId, readerCurrentReferenceId);

            var references = GetReadReferenceTable(readerSession);
            _log.WriteLine($"Read references:\n{references}");
        }

        private static void GetGeneratedSerializer<T>(out IFieldCodec<T> serializer)
        {
            var services = new ServiceCollection().AddSerializer();
            var serviceProvider = services.BuildServiceProvider();
            var codecProvider = serviceProvider.GetRequiredService<CodecProvider>();
            serializer = codecProvider.GetCodec<T>();
        }

        [GenerateSerializer]
        public class ObjectWithNewField
        {
            [Id(0)]
            public string Blah { get; set; }
            [Id(1)]
            public object NewField { get; set; }
            [Id(2)]
            public int Version { get; set; }

            public ObjectWithNewField(string blah, object newField)
            {
                Blah = blah;
                NewField = newField;
                Version = 2;
            }

            public override string ToString() => $"{nameof(Blah)}: {Blah}; {nameof(NewField)}: {NewField}; {nameof(Version)}: {Version}";
        }

        public class ObjectWithNewFieldTypeSerializer : IBaseCodec<ObjectWithNewField>
        {
            public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, ObjectWithNewField obj) where TBufferWriter : IBufferWriter<byte>
            {
                // 不序列化 newField，以模拟由该对象的旧版本生成的二进制数据。
                StringCodec.WriteField(ref writer, 0, obj.Blah);
                Int32Codec.WriteField(ref writer, 2, obj.Version);
            }

            // 使用生成式反序列化器进行反序列化。
            public void Deserialize<TInput>(ref Reader<TInput> reader, ObjectWithNewField obj)
            {
            }
        }

        [GenerateSerializer]
        public class ObjectWithoutNewField
        {
            [Id(0)]
            public string Blah { get; set; }
            [Id(1)]
            public int Version { get; set; }

            public ObjectWithoutNewField(string blah)
            {
                Blah = blah;
                Version = 1;
            }

            public override string ToString() => $"{nameof(Blah)}: {Blah}; {nameof(Version)}: {Version}";
        }

        public class ObjectWithoutNewFieldTypeSerializer : IBaseCodec<ObjectWithoutNewField>
        {
            public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, ObjectWithoutNewField obj) where TBufferWriter : IBufferWriter<byte>
            {
                StringCodec.WriteField(ref writer, 0, obj.Blah);
                Int32Codec.WriteField(ref writer, 1, obj.Version);
                // 序列化新字段，以模拟由该对象的新版本生成的二进制数据。
                ObjectCodec.WriteField(ref writer, 6, "I will be stuck in binary limbo! (I shouldn't be part of the deserialized object)");
            }

            // 使用生成式反序列化器进行反序列化。
            public void Deserialize<TInput>(ref Reader<TInput> reader, ObjectWithoutNewField obj)
            {
            }
        }

        /// <summary>
        /// 注意：该类型的序列化器是手写的。请参阅 <see cref="BaseTypeSerializer" />。
        /// </summary>
        public class BaseType : IEquatable<BaseType>
        {
            public string? BaseTypeString { get; set; }
            public string? AddedLaterString { get; set; }

            public bool Equals(BaseType? other) => other is not null
                    && string.Equals(BaseTypeString, other.BaseTypeString, StringComparison.Ordinal)
                    && string.Equals(AddedLaterString, other.AddedLaterString, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is BaseType baseType && Equals(baseType);

            public override int GetHashCode() => HashCode.Combine(BaseTypeString, AddedLaterString);

            public override string ToString() => $"{nameof(BaseTypeString)}: {BaseTypeString}";
        }

        /// <summary>
        /// 注意：该类型的序列化器是手写的。请参阅 <see cref="SubTypeSerializer" />。
        /// </summary>
        public class SubType : BaseType, IEquatable<SubType>
        {
            // 0
            public string? String { get; set; }

            // 1
            public int Int { get; set; }

            // 3
            public object? Ref { get; set; }

            public bool Equals(SubType? other)
            {
                if (other is null)
                {
                    return false;
                }

                return
                    base.Equals(other)
                    && string.Equals(String, other.String, StringComparison.Ordinal)
                    && Int == other.Int
                    // 经过序列化往返的相等性比较输入会保留 Ref 是否存在的信息。
                    && (ReferenceEquals(Ref, other.Ref) || Ref!.Equals(other.Ref));
            }

            public override string ToString()
            {
                string refString = Ref == this ? "[this]" : $"[{Ref?.ToString() ?? "null"}]";
                return $"{base.ToString()}, {nameof(String)}: {String}, {nameof(Int)}: {Int}, Ref: {refString}";
            }

            public override bool Equals(object? obj) => obj is SubType subType && Equals(subType);

            public override int GetHashCode()
            {
                // 用这个有些特别的小技巧避免堆栈溢出。
                if (ReferenceEquals(Ref, this))
                {
                    return HashCode.Combine(base.GetHashCode(), String, Int);
                }

                return HashCode.Combine(base.GetHashCode(), String, Int, Ref);
            }
        }

        public class SubTypeSerializer : IBaseCodec<SubType>
        {
            private readonly IBaseCodec<BaseType> _baseTypeSerializer;
            private readonly IFieldCodec<string> _stringCodec;
            private readonly IFieldCodec<int> _intCodec;
            private readonly IFieldCodec<object> _objectCodec;

            public SubTypeSerializer(IBaseCodec<BaseType> baseTypeSerializer, IFieldCodec<string> stringCodec, IFieldCodec<int> intCodec, IFieldCodec<object> objectCodec)
            {
                _baseTypeSerializer = OrleansGeneratedCodeHelper.UnwrapService(this, baseTypeSerializer);
                _stringCodec = OrleansGeneratedCodeHelper.UnwrapService(this, stringCodec);
                _intCodec = OrleansGeneratedCodeHelper.UnwrapService(this, intCodec);
                _objectCodec = OrleansGeneratedCodeHelper.UnwrapService(this, objectCodec);
            }

            public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, SubType obj) where TBufferWriter : IBufferWriter<byte>
            {
                _baseTypeSerializer.Serialize(ref writer, obj);
                writer.WriteEndBase(); // 基类对象的写入已完成。

                // 尽管编解码器的旧版签名为不可空类型，但它支持 null 负载。
                _stringCodec.WriteField(ref writer, 0, typeof(string), obj.String!);
                _intCodec.WriteField(ref writer, 1, typeof(int), obj.Int);
                _objectCodec.WriteField(ref writer, 1, typeof(object), obj.Ref!);
                _intCodec.WriteField(ref writer, 1, typeof(int), obj.Int);
                _intCodec.WriteField(ref writer, 409, typeof(int), obj.Int);
                /*writer.WriteFieldHeader(session, 1025, typeof(Guid), Guid.Empty.GetType(), WireType.Fixed128);
                writer.WriteFieldHeader(session, 1020, typeof(object), typeof(Program), WireType.Reference);*/
            }

            public void Deserialize<TInput>(ref Reader<TInput> reader, SubType obj)
            {
                uint fieldId = 0;
                _baseTypeSerializer.Deserialize(ref reader, obj);
                while (true)
                {
                    var header = reader.ReadFieldHeader();
                    if (header.IsEndBaseOrEndObject)
                    {
                        break;
                    }

                    fieldId += header.FieldIdDelta;
                    switch (fieldId)
                    {
                        case 0:
                            obj.String = _stringCodec.ReadValue(ref reader, header);
                            break;
                        case 1:
                            obj.Int = _intCodec.ReadValue(ref reader, header);
                            break;
                        case 2:
                            obj.Ref = _objectCodec.ReadValue(ref reader, header);
                            break;
                        default:
                            reader.ConsumeUnknownField(header);
                            break;
                    }
                }
            }
        }

        public class BaseTypeSerializer : IBaseCodec<BaseType>
        {
            public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, BaseType obj) where TBufferWriter : IBufferWriter<byte>
            {
                // 尽管编解码器的旧版签名为不可空类型，但它支持 null 负载。
                StringCodec.WriteField(ref writer, 0, obj.BaseTypeString!);
                StringCodec.WriteField(ref writer, 234, obj.AddedLaterString!);
            }

            public void Deserialize<TInput>(ref Reader<TInput> reader, BaseType obj)
            {
                uint fieldId = 0;
                while (true)
                {
                    var header = reader.ReadFieldHeader();
                    if (header.IsEndBaseOrEndObject)
                    {
                        break;
                    }

                    fieldId += header.FieldIdDelta;
                    switch (fieldId)
                    {
                        case 0:
                            obj.BaseTypeString = StringCodec.ReadValue(ref reader, header);
                            break;
                        default:
                            reader.ConsumeUnknownField(header);
                            break;
                    }
                }
            }
        }
    }
}

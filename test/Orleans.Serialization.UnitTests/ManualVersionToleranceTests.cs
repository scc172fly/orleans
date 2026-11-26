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
    /// 测试 Orleans版本在序列化中的耐受能力。
    /// 
    /// 版本容度是 Orleans 的关键特征,允许:
    /// - 在不破坏现有序列数据的情况下,将新字段添加到类型中
    /// - 优雅地删除字段(已移除字段的旧数据仍可被解密)
    /// - 改变类型等级,同时保持兼容性
    /// - 将升级的系统在分布式系统中进行,不中断
    /// 
    /// Orleans 通过以下方式实现版本容忍度:
    /// - 实地序列化(不依赖订单)
    /// - 各种版本保持稳定的明确字段身份
    /// - 在解除航空化期间优异地处理未知领域
    /// - 支持计划演变模式
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

                        // 有意删除这些类型的序列器。 在测试时将添加到此上面 。
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
            Assert.Null(actual.AddedLaterString); // 消毒剂不是“意识到”这个领域,
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

            // 使用无视 ObjectWithNewField.NewField的手工序列器
            // 不对 NewField 进行序列化,以模拟从对象上一个版本创建的二进制
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

                // 使用能解除音量的生成降序器 NewField 
                var value = objectWithNewFieldSerializer.ReadValue(ref reader, initialHeader)!;
                return (value, reader.Position, reader.Session.ReferencedObjects.CurrentReferenceId);
            }

            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();

            _log.WriteLine($"Expect: {expected}\nActual: {actual}");

            Assert.Equal(expected.Blah, actual.Blah);
            objectWithNewFieldSerializer = _codecProvider.GetCodec<ObjectWithNewField>();
            objectWithoutNewFieldSerializer = _codecProvider.GetCodec<ObjectWithoutNewField>();
            Assert.Null(actual.NewField); // 努尔,因为它不应该在二进制
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
            // 使用一个手工连环播放器, 以撰写新字段
            // 正在对一个新字段进行序列序列操作以模拟从对象新版本中创建的二进制
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

                // 使用生成的降序器,它无法将序列化的新字段降序
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
                // 不对 newField 进行序列化,以模拟从对象上一个版本创建的二进制
                StringCodec.WriteField(ref writer, 0, obj.Blah);
                Int32Codec.WriteField(ref writer, 2, obj.Version);
            }

            // 使用生成的去气分解器进行下气化
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
                // 正在对一个新字段进行序列序列操作以模拟从对象新版本创建的二进制
                ObjectCodec.WriteField(ref writer, 6, "I will be stuck in binary limbo! (I shouldn't be part of the deserialized object)");
            }

            // 使用生成的去气分解器进行下气化
            public void Deserialize<TInput>(ref Reader<TInput> reader, ObjectWithoutNewField obj)
            {
            }
        }

        /// <summary>
        /// 注:这种类型的序列器是手动转盘。 <see cref="BaseTypeSerializer" />
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
        /// 注:这种类型的序列器是手动转盘。 <see cref="SubTypeSerializer" />
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
                    // 圆桌的平等投入保留了参考文献是否存在。
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
                // 避免堆叠溢出与这个奇怪的把戏。
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
                writer.WriteEndBase(); // 基对象已完成。

                // 尽管该编码码支持了遗留的不可更改签名,但该编码码仍然支持空有效载荷。
                _stringCodec.WriteField(ref writer, 0, typeof(string), obj.String!);
                _intCodec.WriteField(ref writer, 1, typeof(int), obj.Int);
                _objectCodec.WriteField(ref writer, 1, typeof(object), obj.Ref!);
                _intCodec.WriteField(ref writer, 1, typeof(int), obj.Int);
                _intCodec.WriteField(ref writer, 409, typeof(int), obj.Int);
                /*writer.WriteFieldHeader(届会, 1025, 类型( 指南) ) Guid.Empty.GetType(), WireType.Fixed128);
                writer.WriteFieldHeader(会议,1020,类型(目标),类型(方案), WireType.Reference);*/
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
                // 尽管该编码码支持了遗留的不可更改签名,但该编码码仍然支持空有效载荷。
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

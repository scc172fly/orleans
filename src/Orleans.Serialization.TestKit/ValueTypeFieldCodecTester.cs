using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using Xunit;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization.TestKit
{
    /// <summary>
    /// 提供实地编码测试,将价值类型序列化。
    /// </summary>
    /// <typeparam name="TField">正在序列化的价值类型 。</typeparam>
    /// <typeparam name="TCodec">字段编码类型。</typeparam>
    public abstract class ValueTypeFieldCodecTester<TField, TCodec> : FieldCodecTester<TField, TCodec> where TField : struct where TCodec : class, IFieldCodec<TField>
    {
        /// <summary>
        /// 初始化 <see cref="ValueTypeFieldCodecTester{TField, TCodec}"/> 类的新实例。
        /// </summary>
        /// <param name="output">测试输出助手。</param>
        protected ValueTypeFieldCodecTester(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// 初始化 <see cref="ValueTypeFieldCodecTester{TField, TCodec}"/> 类的新实例。
        /// </summary>
        /// <param name="output">测试输出助手。</param>
        /// <param name="fixture">提供共享序列器服务提供商的固定装置。</param>
        protected ValueTypeFieldCodecTester(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
        {
        }

        /// <summary>
        /// 证明每个测试值都可使用注册的序列值转换器进行四舍五入。
        /// </summary>
        [Fact]
        public void ValueSerializerRoundTrip()
        {
            var serializer = ServiceProvider.GetRequiredService<ValueSerializer<TField>>();
            foreach (var value in TestValues)
            {
                var valueCopy = value;
                var serialized = serializer.SerializeToArray(ref valueCopy);
                var deserializedValue = default(TField);
                serializer.Deserialize(serialized, ref deserializedValue);
                Assert.Equal(value, deserializedValue);
            }
        }

        /// <summary>
        /// 证明每种测试值都可以使用价值序列器供应商进行四舍五入。
        /// </summary>
        [Fact]
        public void DirectAccessValueSerializerRoundTrip()
        {
            foreach (var value in TestValues)
            {
                var valueLocal = value;
                TestRoundTrippedValueViaValueSerializer(ref valueLocal);
            }
        }

        private void TestRoundTrippedValueViaValueSerializer(ref TField original)
        {
            var codecProvider = ServiceProvider.GetRequiredService<IValueSerializerProvider>();
            var serializer = codecProvider.GetValueSerializer<TField>();

            var pipe = new Pipe();
            using var writerSession = SessionPool.GetSession();
            var writer = Writer.Create(pipe.Writer, writerSession);
            var writerCodec = serializer;
            writerCodec.Serialize(ref writer, ref original);
            writer.WriteEndObject();
            writer.Commit();
            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            using var readerSession = SessionPool.GetSession();
            var reader = Reader.Create(readResult.Buffer, readerSession);
            var readerCodec = serializer;
            TField deserialized = default;
            readerCodec.Deserialize(ref reader, ref deserialized);
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Reader.Complete();
            var isEqual = Equals(original, deserialized);
            Assert.True(
                isEqual,
                isEqual ? string.Empty : $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
            Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
        }
    }
}

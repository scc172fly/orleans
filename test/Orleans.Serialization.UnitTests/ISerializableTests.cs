using Orleans.Serialization.Buffers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Xunit;

namespace Orleans.Serialization.UnitTests
{
    /// <summary>
    /// Orleans支持 .NET 可连载界面的测试。
    /// 
    /// Orleans 与可按海关顺序排列的可采用易变号的类型兼容,
    /// 这一点对于下列各方面十分重要:
    /// - 与遗留 .NET 类型相结合
    /// - 需要定制序列逻辑的辅助类型
    /// - 保持与现有序列化模式的兼容性
    /// 
    /// DotNetSerializableCodec 处理可采用下列方式的可实现序列类型:
    /// - 尊重连序回调(OnSerializing、 OnSerialized等)
    /// - 支持流流环境
    /// - 处理序列化构造器
    /// - 为没有 Orleans类型 -- -- 特定序列器的系列器提供后备序列序列化
    /// 
    /// 注:虽然支持兼容性,但倾向于 Orleans的本地序列化
    /// 提高业绩和接受度。
    /// </summary>
    [Trait("Category", "BVT"), Trait("Category", "ISerializable")]
    [Trait("Suite", "BVT")]
    [Trait("Provider", "None")]
    [Trait("Area", "Serialization")]
    public class ISerializableTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SerializerSessionPool _sessionPool;
        private readonly Serializer<object> _serializer;
        private readonly ITestOutputHelper _log;

        public ISerializableTests(ITestOutputHelper log)
        {
            var services = new ServiceCollection();
            _ = services.AddSerializer();
            services.RemoveAll(typeof(TypeResolver));
            services.AddSingleton<TypeResolver>(sp => new BanningTypeResolver(typeof(UnserializableConformingException), typeof(UnserializableNonConformingException)));
            services.AddSingleton<IGeneralizedCodec, DotNetSerializableCodec>();

            _serviceProvider = services.BuildServiceProvider();
            // AddSerializer 保证会场集合已经登记。
            _sessionPool = _serviceProvider.GetService<SerializerSessionPool>()!;
            _serializer = _serviceProvider.GetRequiredService<Serializer<object>>();
            _log = log;
        }

        private object SerializationLoop(object original)
        {
            var pipe = new Pipe();

            using var writerSession = _sessionPool.GetSession();
            var writer = Writer.Create(pipe.Writer, writerSession);
            _serializer.Serialize(original, ref writer);
            _ = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            pipe.Writer.Complete();

            _ = pipe.Reader.TryRead(out var readResult);
            {
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var output = BitStreamFormatter.Format(ref reader);
                _log.WriteLine(output);
            }

            {
                using var readerSession = _sessionPool.GetSession();
                var reader = Reader.Create(readResult.Buffer, readerSession);
                var deserialized = _serializer.Deserialize(ref reader);
                pipe.Reader.AdvanceTo(readResult.Buffer.End);
                pipe.Reader.Complete();

                //Assert.True(Equals(original, deserialized), $"Deserialized value \"{deserialized}\" must equal original value \"{original}\"");
                Assert.Equal(writer.Position, reader.Position);
                Assert.Equal(writerSession.ReferencedObjects.CurrentReferenceId, readerSession.ReferencedObjects.CurrentReferenceId);
                return deserialized!;
            }
        }

        /// <summary>
        /// 测试 <see cref="DotNetSerializableCodec"/> 能够正确序列化对象。
        /// </summary>
        [Fact]
        public void ISerializableObjectWithCallbacks()
        {
            var input = new SimpleISerializableObject
            {
                Payload = "pyjamas"
            };

            // 核实我们的行为符合预期行为
            var result = (SimpleISerializableObject)SerializationLoop(input);
            Assert.Equal(
                new[]
                {
                    "default_ctor",
                    "serializing",
                    "serialized"
                },
                input.History);
            Assert.Equal(3, input.Contexts.Count);

            Assert.Equal(
                new[]
                {
                    "deserializing",
                    "serialization_ctor",
                    "deserialized",
                    "deserialization"
                },
                result.History);
            Assert.Equal(input.Payload, result.Payload, StringComparer.Ordinal);
            Assert.Equal(3, result.Contexts.Count);
        }

        /// <summary>
        /// 测试 <see cref="DotNetSerializableCodec"/> 能够正确序列结构。
        /// </summary>
        [Fact]
        public void ISerializableStructWithCallbacks()
        {
            var input = new SimpleISerializableStruct
            {
                Payload = "pyjamas"
            };

            // 核实我们的行为符合预期行为
            var result = (SimpleISerializableStruct)SerializationLoop(input);
            Assert.Equal(
                new[]
                {
                    "serialization_ctor",
                    "deserialized",
                    "deserialization"
                },
                result.History);
            Assert.Equal(input.Payload, result.Payload, StringComparer.Ordinal);
            Assert.Equal(2, result.Contexts.Count);
        }

        [Fact]
        public void ISerializableStructWithoutDeserializationCallback_RestoresState()
        {
            var input = new PlainISerializableStruct("ordinary struct", 37);

            var result = Assert.IsType<PlainISerializableStruct>(SerializationLoop(input));

            Assert.Equal(input.Payload, result.Payload);
            Assert.Equal(input.Revision, result.Revision);
        }

        [Serializable]
        private readonly struct PlainISerializableStruct : ISerializable
        {
            public PlainISerializableStruct(string payload, int revision)
            {
                Payload = payload;
                Revision = revision;
            }

            private PlainISerializableStruct(SerializationInfo info, StreamingContext context)
            {
                Payload = info.GetString(nameof(Payload))!;
                Revision = info.GetInt32(nameof(Revision));
            }

            public string Payload { get; }
            public int Revision { get; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue(nameof(Payload), Payload);
                info.AddValue(nameof(Revision), Revision);
            }
        }

        private class BaseException : Exception
        {
            public BaseException() { }

            public BaseException(string message, Exception innerException) : base(message, innerException) { }
#if NET8_0_OR_GREATER
            [Obsolete]
#endif
            protected BaseException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
                BaseField = (SimpleISerializableObject?)info.GetValue("BaseField", typeof(SimpleISerializableObject));
            }

            public SimpleISerializableObject? BaseField { get; set; }

#if NET8_0_OR_GREATER
            [Obsolete]
#endif
            public override void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                base.GetObjectData(info, context);
                info.AddValue("BaseField", BaseField, typeof(SimpleISerializableObject));
            }
        }

        [Serializable]
        private class UnserializableConformingException : BaseException
        {
            public string? SubClassField { get; set; }
            public object? SomeObject { get; set; }

            public UnserializableConformingException(string message, Exception innerException) : base(message, innerException) { }

#if NET8_0_OR_GREATER
            [Obsolete]
#endif
            protected UnserializableConformingException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
                SubClassField = info.GetString("SubClassField");
                SomeObject = info.GetValue("SomeObject", typeof(object));
            }

#if NET8_0_OR_GREATER
            [Obsolete]
#endif
            public override void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                base.GetObjectData(info, context);
                info.AddValue("SubClassField", SubClassField);
                info.AddValue("SomeObject", SomeObject, typeof(object));
            }
        }

        public class UnserializableNonConformingException : Exception
        {
            public UnserializableNonConformingException(string message) : base(message)
            { }
        }

        private class BanningTypeResolver : TypeResolver
        {
            private readonly TypeResolver _resolver = new CachedTypeResolver();
            private readonly HashSet<Type> _blockedTypes;

            public BanningTypeResolver(params Type[] blockedTypes)
            {
                _blockedTypes = new HashSet<Type>();
                foreach (var type in blockedTypes ?? Array.Empty<Type>())
                {
                    _blockedTypes.Add(type);
                }
            }

            // 这个测试解决者有意扩大遗留的被封杀类型不可停用的合同。
#pragma warning disable CS8764
            [return: MaybeNull]
            public override Type ResolveType(string name)
            {
                var result = _resolver.ResolveType(name);
                if (_blockedTypes.Contains(result))
                {
                    result = null;
                }

                return result;
            }
#pragma warning restore CS8764

            // 这个测试解决者有意扩大遗留的无法避免的失败合同。
#pragma warning disable CS8765
            public override bool TryResolveType(string name, [MaybeNullWhen(false)] out Type type)
            {
                if (_resolver.TryResolveType(name, out type))
                {
                    if (_blockedTypes.Contains(type))
                    {
                        type = null;
                        return false;
                    }

                    return true;
                }

                return false;
            }
#pragma warning restore CS8765
        }

        [Fact]
        public void Serialize_UnserializableException()
        {
            const string message = "This is a test message";

            var serializer = _serviceProvider.GetRequiredService<Serializer>();

            // 丢出例外, 这样可以让堆叠追踪被显示
            Exception source = Assert.Throws<UnserializableNonConformingException>((Action)(() =>
            {
                throw new UnserializableNonConformingException(message);
            }));

            var serialized = serializer.SerializeToArray(source);
            using var formatterSession = _sessionPool.GetSession();
            var formatted = BitStreamFormatter.Format(serialized, formatterSession);

            object deserialized = serializer.Deserialize<Exception>(serialized)!;

            // 无法序列的例外在往返旅行后类型错误
            var result = Assert.IsAssignableFrom<UnavailableExceptionFallbackException>(deserialized);

            // 无法序列的例外情形经过往返后,例外电文正确无误
            Assert.Contains(message, result.Message);
            Assert.Equal(RuntimeTypeNameFormatter.Format(source.GetType()), result.ExceptionType);

            // 丢出例外, 这样可以让堆叠追踪被显示
            source = Assert.Throws<UnserializableConformingException>((Action)(() =>
            {
                Exception inner;
                try
                {
                    throw new InvalidOperationException("invalid");
                }
                catch (Exception exception)
                {
                    inner = exception;
                }

                throw new UnserializableConformingException(message, inner)
                {
                    SomeObject = new object(),
                    SubClassField = "hoppo",
                    BaseField = new SimpleISerializableObject() { Payload = "payload" }
                };
            }));
            deserialized = serializer.Deserialize<Exception>(serializer.SerializeToArray(source))!;

            // 无法序列的例外在往返旅行后类型错误
            result = Assert.IsAssignableFrom<UnavailableExceptionFallbackException>(deserialized);

            // 无法序列的例外情形经过往返后,例外电文正确无误
            Assert.Contains(message, result.Message);
            Assert.Equal(RuntimeTypeNameFormatter.Format(source.GetType()), result.ExceptionType);

            var inner = Assert.IsType<InvalidOperationException>(result.InnerException);
            Assert.Equal("invalid", inner.Message);

            Assert.True(result.Properties.ContainsKey("SomeObject"));
            var baseField = Assert.IsType<SimpleISerializableObject>(result.Properties["BaseField"]);
            Assert.Equal("payload", baseField.Payload);
        }

        [Serializable]
        public class SimpleISerializableObject : ISerializable, IDeserializationCallback
        {
            private List<string>? _history;
            private List<StreamingContext>? _contexts;

            public SimpleISerializableObject()
            {
                History.Add("default_ctor");
            }

            public SimpleISerializableObject(SerializationInfo info, StreamingContext context)
            {
                History.Add("serialization_ctor");
                Contexts.Add(context);
                Payload = info.GetString(nameof(Payload));
            }

            public List<string> History => _history ??= new List<string>();
            public List<StreamingContext> Contexts => _contexts ??= new List<StreamingContext>();

            public string? Payload { get; set; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                Contexts.Add(context);
                info.AddValue(nameof(Payload), Payload);
            }

            [OnSerializing]
            internal void OnSerializingMethod(StreamingContext context)
            {
                History.Add("serializing");
                Contexts.Add(context);
            }

            [OnSerialized]
            internal void OnSerializedMethod(StreamingContext context)
            {
                History.Add("serialized");
                Contexts.Add(context);
            }

            [OnDeserializing]
            internal void OnDeserializingMethod(StreamingContext context)
            {
                History.Add("deserializing");
                Contexts.Add(context);
            }

            [OnDeserialized]
            internal void OnDeserializedMethod(StreamingContext context)
            {
                History.Add("deserialized");
                Contexts.Add(context);
            }

            void IDeserializationCallback.OnDeserialization(object? sender) => History.Add("deserialization");
        }

        [Serializable]
        public struct SimpleISerializableStruct : ISerializable, IDeserializationCallback
        {
            private List<string>? _history;
            private List<StreamingContext>? _contexts;

            public SimpleISerializableStruct(SerializationInfo info, StreamingContext context)
            {
                _history = null;
                _contexts = null;
                Payload = info.GetString(nameof(Payload));
                History.Add("serialization_ctor");
                Contexts.Add(context);
            }

            public List<string> History => _history ??= new List<string>();
            public List<StreamingContext> Contexts => _contexts ??= new List<StreamingContext>();

            public string? Payload { get; set; }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                Contexts.Add(context);
                info.AddValue(nameof(Payload), Payload);
            }

            [OnSerializing]
            internal void OnSerializingMethod(StreamingContext context)
            {
                History.Add("serializing");
                Contexts.Add(context);
            }

            [OnSerialized]
            internal void OnSerializedMethod(StreamingContext context)
            {
                History.Add("serialized");
                Contexts.Add(context);
            }

            [OnDeserializing]
            internal void OnDeserializingMethod(StreamingContext context)
            {
                History.Add("deserializing");
                Contexts.Add(context);
            }

            [OnDeserialized]
            internal void OnDeserializedMethod(StreamingContext context)
            {
                History.Add("deserialized");
                Contexts.Add(context);
            }

            void IDeserializationCallback.OnDeserialization(object? sender) => History.Add("deserialization");
        }

        [Fact]
        public void ISerializableClassWithPrivateCallbacksAndConstructor_InvokesMembersInExactOrder()
        {
            PrivateCallbacksISerializableClass.ResetHistory();
            var input = new PrivateCallbacksISerializableClass("class order payload", 37);

            var result = Assert.IsType<PrivateCallbacksISerializableClass>(SerializationLoop(input));

            Assert.Equal(
                new[]
                {
                    "on_serializing",
                    "get_object_data",
                    "on_serialized",
                    "on_deserializing",
                    "serialization_ctor",
                    "on_deserialized",
                    "on_deserialization"
                },
                PrivateCallbacksISerializableClass.History);
        }

        [Fact]
        public void ISerializableClassWithPrivateCallbacksAndConstructor_RestoresState()
        {
            PrivateCallbacksISerializableClass.ResetHistory();
            var input = new PrivateCallbacksISerializableClass("class state payload", 73);

            var result = Assert.IsType<PrivateCallbacksISerializableClass>(SerializationLoop(input));

            Assert.Equal("class state payload", result.Payload, StringComparer.Ordinal);
            Assert.Equal(73, result.Revision);
            Assert.Equal("class:class state payload:73", result.ConstructorRestoredState, StringComparer.Ordinal);
        }

        [Fact]
        public void ISerializableStructWithPrivateCallbacksAndConstructor_InvokesMembersInExactOrder()
        {
            PrivateCallbacksISerializableStruct.ResetHistory();
            var input = new PrivateCallbacksISerializableStruct("struct order payload", 41);

            var result = Assert.IsType<PrivateCallbacksISerializableStruct>(SerializationLoop(input));

            Assert.Equal(
                new[]
                {
                    "on_serializing",
                    "get_object_data",
                    "on_serialized",
                    "on_deserializing",
                    "serialization_ctor",
                    "on_deserialized",
                    "on_deserialization"
                },
                PrivateCallbacksISerializableStruct.History);
        }

        [Fact]
        public void ISerializableStructWithPrivateCallbacksAndConstructor_RestoresState()
        {
            PrivateCallbacksISerializableStruct.ResetHistory();
            var input = new PrivateCallbacksISerializableStruct("struct state payload", 89);

            var result = Assert.IsType<PrivateCallbacksISerializableStruct>(SerializationLoop(input));

            Assert.Equal("struct state payload", result.Payload, StringComparer.Ordinal);
            Assert.Equal(89, result.Revision);
            Assert.Equal("struct:struct state payload:89", result.ConstructorRestoredState, StringComparer.Ordinal);
        }

        [Serializable]
        private class PrivateCallbacksISerializableClass : ISerializable, IDeserializationCallback
        {
            private static readonly List<string> CallbackHistory = new List<string>();

            public PrivateCallbacksISerializableClass(string payload, int revision)
            {
                Payload = payload;
                Revision = revision;
                ConstructorRestoredState = "not restored";
            }

            protected PrivateCallbacksISerializableClass(SerializationInfo info, StreamingContext context)
            {
                CallbackHistory.Add("serialization_ctor");
                Payload = info.GetString(nameof(Payload))!;
                Revision = info.GetInt32(nameof(Revision));
                ConstructorRestoredState = $"class:{Payload}:{Revision}";
            }

            public static IReadOnlyList<string> History => CallbackHistory;

            public string Payload { get; private set; }

            public int Revision { get; private set; }

            public string ConstructorRestoredState { get; private set; }

            public static void ResetHistory() => CallbackHistory.Clear();

            void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
            {
                CallbackHistory.Add("get_object_data");
                info.AddValue(nameof(Payload), Payload);
                info.AddValue(nameof(Revision), Revision);
            }

            [OnSerializing]
            private void OnSerializing(StreamingContext context) => CallbackHistory.Add("on_serializing");

            [OnSerialized]
            private void OnSerialized(StreamingContext context) => CallbackHistory.Add("on_serialized");

            [OnDeserializing]
            private void OnDeserializing(StreamingContext context) => CallbackHistory.Add("on_deserializing");

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context) => CallbackHistory.Add("on_deserialized");

            void IDeserializationCallback.OnDeserialization(object? sender) => CallbackHistory.Add("on_deserialization");
        }

        [Serializable]
        private struct PrivateCallbacksISerializableStruct : ISerializable, IDeserializationCallback
        {
            private static readonly List<string> CallbackHistory = new List<string>();

            public PrivateCallbacksISerializableStruct(string payload, int revision)
            {
                Payload = payload;
                Revision = revision;
                ConstructorRestoredState = "not restored";
            }

            private PrivateCallbacksISerializableStruct(SerializationInfo info, StreamingContext context)
            {
                CallbackHistory.Add("serialization_ctor");
                Payload = info.GetString(nameof(Payload))!;
                Revision = info.GetInt32(nameof(Revision));
                ConstructorRestoredState = $"struct:{Payload}:{Revision}";
            }

            public static IReadOnlyList<string> History => CallbackHistory;

            public string Payload { get; private set; }

            public int Revision { get; private set; }

            public string ConstructorRestoredState { get; private set; }

            public static void ResetHistory() => CallbackHistory.Clear();

            void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
            {
                CallbackHistory.Add("get_object_data");
                info.AddValue(nameof(Payload), Payload);
                info.AddValue(nameof(Revision), Revision);
            }

            [OnSerializing]
            private void OnSerializing(StreamingContext context) => CallbackHistory.Add("on_serializing");

            [OnSerialized]
            private void OnSerialized(StreamingContext context) => CallbackHistory.Add("on_serialized");

            [OnDeserializing]
            private void OnDeserializing(StreamingContext context) => CallbackHistory.Add("on_deserializing");

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context) => CallbackHistory.Add("on_deserialized");

            void IDeserializationCallback.OnDeserialization(object? sender) => CallbackHistory.Add("on_deserialization");
        }
    }
}

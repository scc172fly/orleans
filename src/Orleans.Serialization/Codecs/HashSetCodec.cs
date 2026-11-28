using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="HashSet{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class HashSetCodec<T> : IFieldCodec<HashSet<T>>, IBaseCodec<HashSet<T>>
    {
        private readonly Type CodecElementType = typeof(T);
        private readonly Type _comparerType = typeof(IEqualityComparer<T>);
        private readonly ConstructorInfo _baseConstructor;

        private readonly IFieldCodec<T> _fieldCodec;
        private readonly IFieldCodec<IEqualityComparer<T>> _comparerCodec;

        /// <summary>
        /// 初始化 <see cref="HashSetCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        /// <param name="comparerCodec">比较器的编解码器。</param>
        public HashSetCodec(IFieldCodec<T> fieldCodec, IFieldCodec<IEqualityComparer<T>> comparerCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
            _comparerCodec = OrleansGeneratedCodeHelper.UnwrapService(this, comparerCodec);
            _baseConstructor = typeof(HashSet<T>).GetConstructor([typeof(int), typeof(IEqualityComparer<T>)])!;
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] HashSet<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            Serialize(ref writer, value);

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public HashSet<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<HashSet<T>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            HashSet<T>? result = null;
            IEqualityComparer<T>? comparer = null;
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
                        comparer = _comparerCodec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = CreateInstance(length, comparer, reader.Session, placeholderReferenceId);
                        break;
                    case 2:
                        if (result is null)
                            ThrowLengthFieldMissing();

                        result!.Add(_fieldCodec.ReadValue(ref reader, header)!);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            result ??= CreateInstance(0, comparer, reader.Session, placeholderReferenceId);
            return result;
        }

        private static HashSet<T> CreateInstance(int length, IEqualityComparer<T>? comparer, SerializerSession session, uint placeholderReferenceId)
        {
            var result = new HashSet<T>(length, comparer);
            ReferenceCodec.RecordObject(session, result, placeholderReferenceId);
            return result;
        }

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized set is missing its length field.");

        /// <inheritdoc />
        public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, HashSet<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            if (value.Comparer != EqualityComparer<T>.Default)
            {
                _comparerCodec.WriteField(ref writer, 0, _comparerType, value.Comparer);
            }

            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 1, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                    innerFieldIdDelta = 0;
                }
            }
        }

        void IBaseCodec<HashSet<T>>.Deserialize<TInput>(ref Reader<TInput> reader, HashSet<T> value)
        {
            // 如果集合中包含构造函数添加的元素，则将其清空。
            // 如果序列化有效载荷中包含这些元素，则会在后续代码中重新添加它们。
            value.Clear();

            IEqualityComparer<T>? comparer = null;
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
                        comparer = _comparerCodec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        // 通过调用构造函数重新初始化该类。
                        if (comparer is not null)
                        {
                            _baseConstructor.Invoke(value, [length, comparer]);
                        }
                        else
                        {
                            value.EnsureCapacity(length);
                        }

                        break;
                    case 2:
                        value.Add(_fieldCodec.ReadValue(ref reader, header)!);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

        }
    }

    /// <summary>
    /// <see cref="HashSet{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <remarks>
    /// 初始化 <see cref="HashSetCopier{T}"/> 类的新实例。
    /// </remarks>
    /// <param name="valueCopier">元素复制器。</param>
    [RegisterCopier]
    public sealed class HashSetCopier<T>(IDeepCopier<T> valueCopier) : IDeepCopier<HashSet<T>>, IBaseCopier<HashSet<T>>
    {
        private readonly Type _fieldType = typeof(HashSet<T>);
        private readonly IDeepCopier<T> _copier = valueCopier;
        private readonly ConstructorInfo _baseConstructor = typeof(HashSet<T>).GetConstructor([typeof(int), typeof(IEqualityComparer<T>)])!;

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public HashSet<T>? DeepCopy(HashSet<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<HashSet<T>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
            {
                return context.DeepCopy(input);
            }

            result = new(input.Count, input.Comparer);
            context.RecordCopy(input, result);
            foreach (var item in input)
            {
                result.Add(_copier.DeepCopy(item, context)!);
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(HashSet<T> input, HashSet<T> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            // 如果输出集合中包含构造函数添加的元素，则将其清空。
            // 如果序列化有效载荷中包含这些元素，则会在后续代码中重新添加它们。（译注：此处实际是深拷贝，从 input 复制元素，并不读取序列化有效载荷。）
            output.Clear();
            if (input.Comparer != EqualityComparer<T>.Default)
            {
                _baseConstructor.Invoke(output, [input.Count, input.Comparer]);
            }
            else
            {
                output.EnsureCapacity(input.Count);
            }

            foreach (var item in input)
            {
                output.Add(_copier.DeepCopy(item, context)!);
            }
        }
    }
}

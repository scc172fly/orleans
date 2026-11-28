using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Dictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class DictionaryCodec<TKey, TValue> : IFieldCodec<Dictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _keyFieldType = typeof(TKey);
        private readonly Type _valueFieldType = typeof(TValue);

        private readonly IFieldCodec<TKey> _keyCodec;
        private readonly IFieldCodec<TValue> _valueCodec;
        private readonly IFieldCodec<IEqualityComparer<TKey>>? _comparerCodec;

        /// <summary>
        /// 使用 <see cref="EqualityComparer{T}.Default"/> 初始化字典序列化器。
        /// </summary>
        /// <param name="keyCodec">键编解码器。</param>
        /// <param name="valueCodec">值编解码器。</param>
        /// <remarks>指定了自定义比较器的值和有效载荷会产生 <see cref="NotSupportedException"/>。</remarks>
        public DictionaryCodec(IFieldCodec<TKey> keyCodec, IFieldCodec<TValue> valueCodec)
        {
            _keyCodec = OrleansGeneratedCodeHelper.UnwrapService(this, keyCodec);
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
        }

        /// <summary>
        /// 初始化 <see cref="DictionaryCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCodec">键编解码器。</param>
        /// <param name="valueCodec">值编解码器。</param>
        /// <param name="comparerCodec">比较器的编解码器。</param>
        [ActivatorUtilitiesConstructor]
        public DictionaryCodec(
            IFieldCodec<TKey> keyCodec,
            IFieldCodec<TValue> valueCodec,
            IFieldCodec<IEqualityComparer<TKey>> comparerCodec)
        {
            _keyCodec = OrleansGeneratedCodeHelper.UnwrapService(this, keyCodec);
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
            _comparerCodec = OrleansGeneratedCodeHelper.UnwrapService(this, comparerCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Dictionary<TKey, TValue> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            if (value.Comparer is var comparer && comparer != EqualityComparer<TKey>.Default)
            {
                GetComparerCodec().WriteField(ref writer, 0, null!, comparer);
            }

            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 1, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _keyCodec.WriteField(ref writer, innerFieldIdDelta, _keyFieldType, element.Key);
                    _valueCodec.WriteField(ref writer, 0, _valueFieldType, element.Value);
                    innerFieldIdDelta = 0;
                }
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Dictionary<TKey, TValue> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Dictionary<TKey, TValue>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            TKey? key = default;
            var valueExpected = false;
            Dictionary<TKey, TValue>? result = null;
            IEqualityComparer<TKey>? comparer = null;
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
                        comparer = GetComparerCodec().ReadValue(ref reader, header);
                        break;
                    case 1:
                        var length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = CreateInstance(length, comparer, reader.Session, placeholderReferenceId);
                        break;
                    case 2:
                        if (result is null)
                            ThrowLengthFieldMissing();

                        if (!valueExpected)
                        {
                            key = _keyCodec.ReadValue(ref reader, header);
                            valueExpected = true;
                        }
                        else
                        {
                            result!.Add(key!, _valueCodec.ReadValue(ref reader, header)!);
                            valueExpected = false;
                        }
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            result ??= CreateInstance(0, comparer, reader.Session, placeholderReferenceId);
            return result;
        }

        private Dictionary<TKey, TValue> CreateInstance(int length, IEqualityComparer<TKey>? comparer, SerializerSession session, uint placeholderReferenceId)
        {
            var result = new Dictionary<TKey, TValue>(length, comparer);
            ReferenceCodec.RecordObject(session, result, placeholderReferenceId);
            return result;
        }

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized dictionary is missing its length field.");

        private IFieldCodec<IEqualityComparer<TKey>> GetComparerCodec()
            => _comparerCodec ?? throw new NotSupportedException($"The generated context registration for {typeof(Dictionary<TKey, TValue>)} supports EqualityComparer<{typeof(TKey)}>.Default. Register a codec with comparer support to serialize a custom comparer.");
    }

    /// <summary>
    /// <see cref="Dictionary{TKey, TValue}"/> 的复制器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class DictionaryCopier<TKey, TValue> : IDeepCopier<Dictionary<TKey, TValue>>, IBaseCopier<Dictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;
        private readonly ConstructorInfo _baseConstructor;

        /// <summary>
        /// 初始化 <see cref="DictionaryCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">键复制器。</param>
        /// <param name="valueCopier">值复制器。</param>
        public DictionaryCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = keyCopier;
            _valueCopier = valueCopier;
            _baseConstructor = typeof(Dictionary<TKey, TValue>).GetConstructor([typeof(int), typeof(IEqualityComparer<TKey>)])!;
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Dictionary<TKey, TValue>? DeepCopy(Dictionary<TKey, TValue>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<Dictionary<TKey, TValue>>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() != typeof(Dictionary<TKey, TValue>))
            {
                return context.DeepCopy(input);
            }

            result = new Dictionary<TKey, TValue>(input.Count, input.Comparer);
            context.RecordCopy(input, result);
            foreach (var pair in input)
            {
                result[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }

            return result;
        }

        /// <inheritdoc/>
        public void DeepCopy(Dictionary<TKey, TValue> input, Dictionary<TKey, TValue> output, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(input);
            ArgumentNullExceptionPolyfill.ThrowIfNull(output);
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            output.Clear();
            if (input.Comparer != EqualityComparer<TKey>.Default)
            {
                _baseConstructor.Invoke(output, [input.Count, input.Comparer]);
            }
            else
            {
                output.EnsureCapacity(input.Count);
            }

            foreach (var pair in input)
            {
                output[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
            }
        }
    }

    /// <summary>
    /// <see cref="Dictionary{TKey, TValue}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="TKey">键类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class DictionaryBaseCodec<TKey, TValue> : IBaseCodec<Dictionary<TKey, TValue>> where TKey : notnull
    {
        private readonly Type _keyFieldType = typeof(TKey);
        private readonly Type _valueFieldType = typeof(TValue);

        private readonly IFieldCodec<TKey> _keyCodec;
        private readonly IFieldCodec<TValue> _valueCodec;
        private readonly IFieldCodec<IEqualityComparer<TKey>> _comparerCodec;
        private readonly ConstructorInfo _baseConstructor;

        /// <summary>
        /// 初始化 <see cref="DictionaryCodec{TKey, TValue}"/> 类的新实例。（译注：实际构造函数属于 DictionaryBaseCodec{TKey, TValue}。）
        /// </summary>
        /// <param name="keyCodec">键编解码器。</param>
        /// <param name="valueCodec">值编解码器。</param>
        /// <param name="comparerCodec">比较器的编解码器。</param>
        public DictionaryBaseCodec(
            IFieldCodec<TKey> keyCodec,
            IFieldCodec<TValue> valueCodec,
            IFieldCodec<IEqualityComparer<TKey>> comparerCodec)
        {
            _keyCodec = OrleansGeneratedCodeHelper.UnwrapService(this, keyCodec);
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
            _comparerCodec = OrleansGeneratedCodeHelper.UnwrapService(this, comparerCodec);
            _baseConstructor = typeof(Dictionary<TKey, TValue>).GetConstructor([typeof(int), typeof(IEqualityComparer<TKey>)])!;
        }

        void IBaseCodec<Dictionary<TKey, TValue>>.Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, Dictionary<TKey, TValue> value)
        {
            if (value.Comparer is var comparer && comparer != EqualityComparer<TKey>.Default)
            {
                _comparerCodec.WriteField(ref writer, 0, null!, comparer);
            }

            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 1, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _keyCodec.WriteField(ref writer, innerFieldIdDelta, _keyFieldType, element.Key);
                    _valueCodec.WriteField(ref writer, 0, _valueFieldType, element.Value);
                    innerFieldIdDelta = 0;
                }
            }
        }

        void IBaseCodec<Dictionary<TKey, TValue>>.Deserialize<TInput>(ref Reader<TInput> reader, Dictionary<TKey, TValue> value)
        {
            // 如果字典的默认构造函数已经添加了某些值，则清除它们。
            // 如果序列化有效载荷中包含这些条目，则会在后续代码中重新添加它们。
            value.Clear();

            TKey? key = default;
            var valueExpected = false;
            IEqualityComparer<TKey>? comparer = null;
            uint fieldId = 0;
            bool hasLengthField = false;
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

                        hasLengthField = true;
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
                        if (!hasLengthField)
                        {
                            ThrowLengthFieldMissing();
                        }

                        if (!valueExpected)
                        {
                            key = _keyCodec.ReadValue(ref reader, header);
                            valueExpected = true;
                        }
                        else
                        {
                            value.Add(key!, _valueCodec.ReadValue(ref reader, header)!);
                            valueExpected = false;
                        }
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }
        }

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized dictionary is missing its length field.");

    }
}

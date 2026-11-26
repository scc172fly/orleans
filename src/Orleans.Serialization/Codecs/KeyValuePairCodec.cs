using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="KeyValuePair{TKey, TValue}"/>的序列器。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterSerializer]
    public sealed class KeyValuePairCodec<TKey, TValue> : IFieldCodec<KeyValuePair<TKey, TValue>>
    {
        private readonly IFieldCodec<TKey> _keyCodec;
        private readonly IFieldCodec<TValue> _valueCodec;
        private readonly Type CodecKeyType = typeof(TKey);
        private readonly Type CodecValueType = typeof(TValue);

        /// <summary>
        /// 初始化 <see cref="KeyValuePairCodec{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCodec">密钥编码器。</param>
        /// <param name="valueCodec">值代码。</param>
        public KeyValuePairCodec(IFieldCodec<TKey> keyCodec, IFieldCodec<TValue> valueCodec)
        {
            _keyCodec = OrleansGeneratedCodeHelper.UnwrapService(this, keyCodec);
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] KeyValuePair<TKey, TValue> value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _keyCodec.WriteField(ref writer, 0, CodecKeyType, value.Key);
            _valueCodec.WriteField(ref writer, 1, CodecValueType, value.Value);

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public KeyValuePair<TKey, TValue> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            var key = default(TKey);
            var value = default(TValue);
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
                        key = _keyCodec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        value = _valueCodec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return new KeyValuePair<TKey, TValue>(key!, value!);
        }
    }

    /// <summary>
    /// <see cref="KeyValuePair{TKey, TValue}"/>的复印机。
    /// </summary>
    /// <typeparam name="TKey">密钥类型。</typeparam>
    /// <typeparam name="TValue">值类型。</typeparam>
    [RegisterCopier]
    public sealed class KeyValuePairCopier<TKey, TValue> : IDeepCopier<KeyValuePair<TKey, TValue>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<TKey> _keyCopier;
        private readonly IDeepCopier<TValue> _valueCopier;

        /// <summary>
        /// 初始化 <see cref="KeyValuePairCopier{TKey, TValue}"/> 类的新实例。
        /// </summary>
        /// <param name="keyCopier">钥匙复印机。</param>
        /// <param name="valueCopier">价值复印机。</param>
        public KeyValuePairCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
        {
            _keyCopier = OrleansGeneratedCodeHelper.UnwrapService(this, keyCopier);
            _valueCopier = OrleansGeneratedCodeHelper.UnwrapService(this, valueCopier);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._keyCopier) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._valueCopier));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy((KeyValuePair<TKey, TValue>)input!, context);

        /// <inheritdoc/>
        public KeyValuePair<TKey, TValue> DeepCopy(KeyValuePair<TKey, TValue> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            return new(OrleansGeneratedCodeHelper.IsShallowCopyable(_keyCopier) ? input.Key : _keyCopier.DeepCopy(input.Key, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_valueCopier) ? input.Value : _valueCopier.DeepCopy(input.Value, context)!);
        }
    }
}

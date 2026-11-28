using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ValueTuple"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class ValueTupleCodec : IFieldCodec<ValueTuple>
    {
        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] ValueTuple value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.VarInt);
            writer.WriteVarUInt7(0);
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public ValueTuple ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireType(WireType.VarInt);

            ReferenceCodec.MarkValueField(reader.Session);
            var length = reader.ReadVarUInt32();
            if (length != 0) throw new UnexpectedLengthPrefixValueException(nameof(ValueTuple), 0, length);

            return default;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T> : IFieldCodec<ValueTuple<T>>
    {
        private readonly Type ElementType1 = typeof(T);

        private readonly IFieldCodec<T> _valueCodec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="valueCodec">值编解码器。</param>
        public ValueTupleCodec(IFieldCodec<T> valueCodec)
        {
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] ValueTuple<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _valueCodec.WriteField(ref writer, 1, ElementType1, value.Item1);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public ValueTuple<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            var item1 = default(T);
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
                    case 1:
                        item1 = _valueCodec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return new ValueTuple<T>(item1!);
        }
    }

    /// <summary>
    /// <see cref="ValueTuple"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ValueTupleCopier : IDeepCopier<ValueTuple>, IOptionalDeepCopier
    {
        /// <inheritdoc />
        public bool IsShallowCopyable() => true;

        /// <inheritdoc />
        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => input;

        /// <inheritdoc />
        public ValueTuple DeepCopy(ValueTuple input, CopyContext context) => input;
    }

    /// <summary>
    /// <see cref="ValueTuple{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T> : IDeepCopier<ValueTuple<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">复制器。</param>
        public ValueTupleCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy((ValueTuple<T>)input!, context);

        /// <inheritdoc />
        public ValueTuple<T> DeepCopy(ValueTuple<T> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            input.Item1 = _copier.DeepCopy(input.Item1, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2> : IFieldCodec<ValueTuple<T1, T2>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        public ValueTupleCodec(IFieldCodec<T1> item1Codec, IFieldCodec<T2> item2Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public (T1, T2) ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2> : IDeepCopier<ValueTuple<T1, T2>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        public ValueTupleCopier(IDeepCopier<T1> copier1, IDeepCopier<T2> copier2)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2> DeepCopy(ValueTuple<T1, T2> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3> : IFieldCodec<ValueTuple<T1, T2, T3>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2, T3) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public (T1, T2, T3) ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2, T3) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3> : IDeepCopier<ValueTuple<T1, T2, T3>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2, T3))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3> DeepCopy(ValueTuple<T1, T2, T3> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3, T4> : IFieldCodec<ValueTuple<T1, T2, T3, T4>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);
        private readonly Type ElementType4 = typeof(T4);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;
        private readonly IFieldCodec<T4> _item4Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3, T4}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec,
            IFieldCodec<T4> item4Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
            _item4Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item4Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2, T3, T4) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public (T1, T2, T3, T4) ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2, T3, T4) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    case 4: res.Item4 = _item4Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3, T4> : IDeepCopier<ValueTuple<T1, T2, T3, T4>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3, T4}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3,
            IDeepCopier<T4> copier4)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
            _copier4 = OrleansGeneratedCodeHelper.UnwrapService(this, copier4);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier4));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2, T3, T4))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3, T4> DeepCopy(ValueTuple<T1, T2, T3, T4> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4)) input.Item4 = _copier4.DeepCopy(input.Item4, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3, T4, T5> : IFieldCodec<ValueTuple<T1, T2, T3, T4, T5>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);
        private readonly Type ElementType4 = typeof(T4);
        private readonly Type ElementType5 = typeof(T5);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;
        private readonly IFieldCodec<T4> _item4Codec;
        private readonly IFieldCodec<T5> _item5Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3, T4, T5}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec,
            IFieldCodec<T4> item4Codec,
            IFieldCodec<T5> item5Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
            _item4Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item4Codec);
            _item5Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item5Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2, T3, T4, T5) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);
            _item5Codec.WriteField(ref writer, 1, ElementType5, value.Item5);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public (T1, T2, T3, T4, T5) ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2, T3, T4, T5) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    case 4: res.Item4 = _item4Codec.ReadValue(ref reader, header)!; break;
                    case 5: res.Item5 = _item5Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3, T4, T5> : IDeepCopier<ValueTuple<T1, T2, T3, T4, T5>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3, T4, T5}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3,
            IDeepCopier<T4> copier4,
            IDeepCopier<T5> copier5)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
            _copier4 = OrleansGeneratedCodeHelper.UnwrapService(this, copier4);
            _copier5 = OrleansGeneratedCodeHelper.UnwrapService(this, copier5);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier4) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier5));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2, T3, T4, T5))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3, T4, T5> DeepCopy(ValueTuple<T1, T2, T3, T4, T5> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4)) input.Item4 = _copier4.DeepCopy(input.Item4, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5)) input.Item5 = _copier5.DeepCopy(input.Item5, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3, T4, T5, T6> : IFieldCodec<ValueTuple<T1, T2, T3, T4, T5, T6>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);
        private readonly Type ElementType4 = typeof(T4);
        private readonly Type ElementType5 = typeof(T5);
        private readonly Type ElementType6 = typeof(T6);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;
        private readonly IFieldCodec<T4> _item4Codec;
        private readonly IFieldCodec<T5> _item5Codec;
        private readonly IFieldCodec<T6> _item6Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3, T4, T5, T6}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec,
            IFieldCodec<T4> item4Codec,
            IFieldCodec<T5> item5Codec,
            IFieldCodec<T6> item6Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
            _item4Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item4Codec);
            _item5Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item5Codec);
            _item6Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item6Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2, T3, T4, T5, T6) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);
            _item5Codec.WriteField(ref writer, 1, ElementType5, value.Item5);
            _item6Codec.WriteField(ref writer, 1, ElementType6, value.Item6);


            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public (T1, T2, T3, T4, T5, T6) ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2, T3, T4, T5, T6) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    case 4: res.Item4 = _item4Codec.ReadValue(ref reader, header)!; break;
                    case 5: res.Item5 = _item5Codec.ReadValue(ref reader, header)!; break;
                    case 6: res.Item6 = _item6Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3, T4, T5, T6> : IDeepCopier<ValueTuple<T1, T2, T3, T4, T5, T6>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3, T4, T5, T6}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3,
            IDeepCopier<T4> copier4,
            IDeepCopier<T5> copier5,
            IDeepCopier<T6> copier6)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
            _copier4 = OrleansGeneratedCodeHelper.UnwrapService(this, copier4);
            _copier5 = OrleansGeneratedCodeHelper.UnwrapService(this, copier5);
            _copier6 = OrleansGeneratedCodeHelper.UnwrapService(this, copier6);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier4) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier5) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier6));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2, T3, T4, T5, T6))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3, T4, T5, T6> DeepCopy(ValueTuple<T1, T2, T3, T4, T5, T6> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4)) input.Item4 = _copier4.DeepCopy(input.Item4, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5)) input.Item5 = _copier5.DeepCopy(input.Item5, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6)) input.Item6 = _copier6.DeepCopy(input.Item6, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3, T4, T5, T6, T7> : IFieldCodec<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);
        private readonly Type ElementType4 = typeof(T4);
        private readonly Type ElementType5 = typeof(T5);
        private readonly Type ElementType6 = typeof(T6);
        private readonly Type ElementType7 = typeof(T7);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;
        private readonly IFieldCodec<T4> _item4Codec;
        private readonly IFieldCodec<T5> _item5Codec;
        private readonly IFieldCodec<T6> _item6Codec;
        private readonly IFieldCodec<T7> _item7Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3, T4, T5, T6, T7}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        /// <param name="item7Codec"><typeparamref name="T7"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec,
            IFieldCodec<T4> item4Codec,
            IFieldCodec<T5> item5Codec,
            IFieldCodec<T6> item6Codec,
            IFieldCodec<T7> item7Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
            _item4Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item4Codec);
            _item5Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item5Codec);
            _item6Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item6Codec);
            _item7Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item7Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] (T1, T2, T3, T4, T5, T6, T7) value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);
            _item5Codec.WriteField(ref writer, 1, ElementType5, value.Item5);
            _item6Codec.WriteField(ref writer, 1, ElementType6, value.Item6);
            _item7Codec.WriteField(ref writer, 1, ElementType7, value.Item7);


            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public (T1, T2, T3, T4, T5, T6, T7) ReadValue<TInput>(
            ref Reader<TInput> reader,
            Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            (T1, T2, T3, T4, T5, T6, T7) res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    case 4: res.Item4 = _item4Codec.ReadValue(ref reader, header)!; break;
                    case 5: res.Item5 = _item5Codec.ReadValue(ref reader, header)!; break;
                    case 6: res.Item6 = _item6Codec.ReadValue(ref reader, header)!; break;
                    case 7: res.Item7 = _item7Codec.ReadValue(ref reader, header)!; break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3, T4, T5, T6, T7> : IDeepCopier<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;
        private readonly IDeepCopier<T7> _copier7;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3, T4, T5, T6, T7}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        /// <param name="copier7"><typeparamref name="T7"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3,
            IDeepCopier<T4> copier4,
            IDeepCopier<T5> copier5,
            IDeepCopier<T6> copier6,
            IDeepCopier<T7> copier7)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
            _copier4 = OrleansGeneratedCodeHelper.UnwrapService(this, copier4);
            _copier5 = OrleansGeneratedCodeHelper.UnwrapService(this, copier5);
            _copier6 = OrleansGeneratedCodeHelper.UnwrapService(this, copier6);
            _copier7 = OrleansGeneratedCodeHelper.UnwrapService(this, copier7);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier4) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier5) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier6) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier7));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy(((T1, T2, T3, T4, T5, T6, T7))input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3, T4, T5, T6, T7> DeepCopy(ValueTuple<T1, T2, T3, T4, T5, T6, T7> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4)) input.Item4 = _copier4.DeepCopy(input.Item4, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5)) input.Item5 = _copier5.DeepCopy(input.Item5, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6)) input.Item6 = _copier6.DeepCopy(input.Item6, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier7)) input.Item7 = _copier7.DeepCopy(input.Item7, context)!;
            return input;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, T8}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    /// <typeparam name="T8">元组第八个分量的类型。（译注：T8 对应 Rest，承载第八个及后续元素的嵌套元组。）</typeparam>
    [RegisterSerializer]
    public sealed class ValueTupleCodec<T1, T2, T3, T4, T5, T6, T7, T8> : IFieldCodec<ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>> where T8 : struct
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);
        private readonly Type ElementType4 = typeof(T4);
        private readonly Type ElementType5 = typeof(T5);
        private readonly Type ElementType6 = typeof(T6);
        private readonly Type ElementType7 = typeof(T7);
        private readonly Type ElementType8 = typeof(T8);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;
        private readonly IFieldCodec<T4> _item4Codec;
        private readonly IFieldCodec<T5> _item5Codec;
        private readonly IFieldCodec<T6> _item6Codec;
        private readonly IFieldCodec<T7> _item7Codec;
        private readonly IFieldCodec<T8> _item8Codec;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCodec{T1, T2, T3, T4, T5, T6, T7, T8}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        /// <param name="item7Codec"><typeparamref name="T7"/> 的编解码器。</param>
        /// <param name="item8Codec"><typeparamref name="T8"/> 的编解码器。</param>
        public ValueTupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec,
            IFieldCodec<T4> item4Codec,
            IFieldCodec<T5> item5Codec,
            IFieldCodec<T6> item6Codec,
            IFieldCodec<T7> item7Codec,
            IFieldCodec<T8> item8Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
            _item4Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item4Codec);
            _item5Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item5Codec);
            _item6Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item6Codec);
            _item7Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item7Codec);
            _item8Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item8Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(
            ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);
            _item5Codec.WriteField(ref writer, 1, ElementType5, value.Item5);
            _item6Codec.WriteField(ref writer, 1, ElementType6, value.Item6);
            _item7Codec.WriteField(ref writer, 1, ElementType7, value.Item7);
            _item8Codec.WriteField(ref writer, 1, ElementType8, value.Rest);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> ReadValue<TInput>(ref Reader<TInput> reader,
            Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> res = default;
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
                    case 1: res.Item1 = _item1Codec.ReadValue(ref reader, header)!; break;
                    case 2: res.Item2 = _item2Codec.ReadValue(ref reader, header)!; break;
                    case 3: res.Item3 = _item3Codec.ReadValue(ref reader, header)!; break;
                    case 4: res.Item4 = _item4Codec.ReadValue(ref reader, header)!; break;
                    case 5: res.Item5 = _item5Codec.ReadValue(ref reader, header)!; break;
                    case 6: res.Item6 = _item6Codec.ReadValue(ref reader, header)!; break;
                    case 7: res.Item7 = _item7Codec.ReadValue(ref reader, header)!; break;
                    case 8: res.Rest = _item8Codec.ReadValue(ref reader, header); break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return res;
        }
    }

    /// <summary>
    /// <see cref="ValueTuple{T1, T2, T3, T4, T5, T6, T7, T8}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    /// <typeparam name="T8">元组第八个分量的类型。（译注：T8 对应 Rest，承载第八个及后续元素的嵌套元组。）</typeparam>
    [RegisterCopier]
    public sealed class ValueTupleCopier<T1, T2, T3, T4, T5, T6, T7, T8> : IDeepCopier<ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>>, IOptionalDeepCopier where T8 : struct
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;
        private readonly IDeepCopier<T7> _copier7;
        private readonly IDeepCopier<T8> _copier8;

        /// <summary>
        /// 初始化 <see cref="ValueTupleCopier{T1, T2, T3, T4, T5, T6, T7, T8}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        /// <param name="copier7"><typeparamref name="T7"/> 的复制器。</param>
        /// <param name="copier8"><typeparamref name="T8"/> 的复制器。</param>
        public ValueTupleCopier(
            IDeepCopier<T1> copier1,
            IDeepCopier<T2> copier2,
            IDeepCopier<T3> copier3,
            IDeepCopier<T4> copier4,
            IDeepCopier<T5> copier5,
            IDeepCopier<T6> copier6,
            IDeepCopier<T7> copier7,
            IDeepCopier<T8> copier8)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
            _copier3 = OrleansGeneratedCodeHelper.UnwrapService(this, copier3);
            _copier4 = OrleansGeneratedCodeHelper.UnwrapService(this, copier4);
            _copier5 = OrleansGeneratedCodeHelper.UnwrapService(this, copier5);
            _copier6 = OrleansGeneratedCodeHelper.UnwrapService(this, copier6);
            _copier7 = OrleansGeneratedCodeHelper.UnwrapService(this, copier7);
            _copier8 = OrleansGeneratedCodeHelper.UnwrapService(this, copier8);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier3) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier4) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier5) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier6) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier7) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier8));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => IsShallowCopyable() ? input : DeepCopy((ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>)input!, context);

        /// <inheritdoc />
        public ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> DeepCopy(ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> input, CopyContext context)
        {
            if (IsShallowCopyable()) return input;

            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1)) input.Item1 = _copier1.DeepCopy(input.Item1, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2)) input.Item2 = _copier2.DeepCopy(input.Item2, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3)) input.Item3 = _copier3.DeepCopy(input.Item3, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4)) input.Item4 = _copier4.DeepCopy(input.Item4, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5)) input.Item5 = _copier5.DeepCopy(input.Item5, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6)) input.Item6 = _copier6.DeepCopy(input.Item6, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier7)) input.Item7 = _copier7.DeepCopy(input.Item7, context)!;
            if (!OrleansGeneratedCodeHelper.IsShallowCopyable(_copier8)) input.Rest = _copier8.DeepCopy(input.Rest, context);
            return input;
        }
    }
}

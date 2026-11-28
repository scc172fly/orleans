using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Tuple{T1}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T> : IFieldCodec<Tuple<T>>
    {
        private readonly Type CodecElementType = typeof(T);

        private readonly IFieldCodec<T> _valueCodec;

        /// <summary>
        /// 初始化 <see cref="TupleCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="valueCodec">值编解码器。</param>
        public TupleCodec(IFieldCodec<T> valueCodec)
        {
            _valueCodec = OrleansGeneratedCodeHelper.UnwrapService(this, valueCodec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _valueCodec.WriteField(ref writer, 1, CodecElementType, value.Item1);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Tuple<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
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

            var result = new Tuple<T>(item1!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T> : IDeepCopier<Tuple<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T>);
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">复制器。</param>
        public TupleCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T>? DeepCopy(Tuple<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(_copier.DeepCopy(input.Item1, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2> : IFieldCodec<Tuple<T1, T2>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;

        /// <summary>
        /// 初始化 <see cref="TupleCodec{T1, T2}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        public TupleCodec(IFieldCodec<T1> item1Codec, IFieldCodec<T2> item2Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Tuple<T1, T2> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2>(item1!, item2!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2> : IDeepCopier<Tuple<T1, T2>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        public TupleCopier(IDeepCopier<T1> copier1, IDeepCopier<T2> copier2)
        {
            _copier1 = OrleansGeneratedCodeHelper.UnwrapService(this, copier1);
            _copier2 = OrleansGeneratedCodeHelper.UnwrapService(this, copier2);
        }

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier1) && OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier2));

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2>? DeepCopy(Tuple<T1, T2>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2, T3> : IFieldCodec<Tuple<T1, T2, T3>>
    {
        private readonly Type ElementType1 = typeof(T1);
        private readonly Type ElementType2 = typeof(T2);
        private readonly Type ElementType3 = typeof(T3);

        private readonly IFieldCodec<T1> _item1Codec;
        private readonly IFieldCodec<T2> _item2Codec;
        private readonly IFieldCodec<T3> _item3Codec;

        /// <summary>
        /// 初始化 <see cref="TupleCodec{T1, T2, T3}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        public TupleCodec(
            IFieldCodec<T1> item1Codec,
            IFieldCodec<T2> item2Codec,
            IFieldCodec<T3> item3Codec)
        {
            _item1Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item1Codec);
            _item2Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item2Codec);
            _item3Codec = OrleansGeneratedCodeHelper.UnwrapService(this, item3Codec);
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Tuple<T1, T2, T3> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3>(item1!, item2!, item3!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2, T3> : IDeepCopier<Tuple<T1, T2, T3>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3>? DeepCopy(Tuple<T1, T2, T3>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2, T3, T4> : IFieldCodec<Tuple<T1, T2, T3, T4>>
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
        /// 初始化 <see cref="TupleCodec{T1, T2, T3, T4}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        public TupleCodec(
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
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3, T4> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            _item1Codec.WriteField(ref writer, 1, ElementType1, value.Item1);
            _item2Codec.WriteField(ref writer, 1, ElementType2, value.Item2);
            _item3Codec.WriteField(ref writer, 1, ElementType3, value.Item3);
            _item4Codec.WriteField(ref writer, 1, ElementType4, value.Item4);

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Tuple<T1, T2, T3, T4> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3, T4>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
            var item4 = default(T4);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        item4 = _item4Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3, T4>(item1!, item2!, item3!, item4!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2, T3, T4> : IDeepCopier<Tuple<T1, T2, T3, T4>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3, T4>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3, T4}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3, T4>? DeepCopy(Tuple<T1, T2, T3, T4>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3, T4>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4) ? input.Item4 : _copier4.DeepCopy(input.Item4, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2, T3, T4, T5> : IFieldCodec<Tuple<T1, T2, T3, T4, T5>>
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
        /// 初始化 <see cref="TupleCodec{T1, T2, T3, T4, T5}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        public TupleCodec(
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
            [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3, T4, T5> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
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
        public Tuple<T1, T2, T3, T4, T5> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3, T4, T5>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
            var item4 = default(T4);
            var item5 = default(T5);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        item4 = _item4Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        item5 = _item5Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3, T4, T5>(item1!, item2!, item3!, item4!, item5!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2, T3, T4, T5> : IDeepCopier<Tuple<T1, T2, T3, T4, T5>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3, T4, T5>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3, T4, T5}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3, T4, T5>? DeepCopy(Tuple<T1, T2, T3, T4, T5>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3, T4, T5>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4) ? input.Item4 : _copier4.DeepCopy(input.Item4, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5) ? input.Item5 : _copier5.DeepCopy(input.Item5, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2, T3, T4, T5, T6> : IFieldCodec<Tuple<T1, T2, T3, T4, T5, T6>>
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
        /// 初始化 <see cref="TupleCodec{T1, T2, T3, T4, T5, T6}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        public TupleCodec(
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
            [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3, T4, T5, T6> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
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
        public Tuple<T1, T2, T3, T4, T5, T6> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3, T4, T5, T6>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
            var item4 = default(T4);
            var item5 = default(T5);
            var item6 = default(T6);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        item4 = _item4Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        item5 = _item5Codec.ReadValue(ref reader, header);
                        break;
                    case 6:
                        item6 = _item6Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3, T4, T5, T6>(item1!, item2!, item3!, item4!, item5!, item6!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2, T3, T4, T5, T6> : IDeepCopier<Tuple<T1, T2, T3, T4, T5, T6>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3, T4, T5, T6>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3, T4, T5, T6}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3, T4, T5, T6>? DeepCopy(Tuple<T1, T2, T3, T4, T5, T6>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3, T4, T5, T6>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4) ? input.Item4 : _copier4.DeepCopy(input.Item4, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5) ? input.Item5 : _copier5.DeepCopy(input.Item5, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6) ? input.Item6 : _copier6.DeepCopy(input.Item6, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6, T7}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    [RegisterSerializer]
    public sealed class TupleCodec<T1, T2, T3, T4, T5, T6, T7> : IFieldCodec<Tuple<T1, T2, T3, T4, T5, T6, T7>>
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
        /// 初始化 <see cref="TupleCodec{T1, T2, T3, T4, T5, T6, T7}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        /// <param name="item7Codec"><typeparamref name="T7"/> 的编解码器。</param>
        public TupleCodec(
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
            [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3, T4, T5, T6, T7> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
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
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Tuple<T1, T2, T3, T4, T5, T6, T7> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3, T4, T5, T6, T7>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
            var item4 = default(T4);
            var item5 = default(T5);
            var item6 = default(T6);
            var item7 = default(T7);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        item4 = _item4Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        item5 = _item5Codec.ReadValue(ref reader, header);
                        break;
                    case 6:
                        item6 = _item6Codec.ReadValue(ref reader, header);
                        break;
                    case 7:
                        item7 = _item7Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3, T4, T5, T6, T7>(item1!, item2!, item3!, item4!, item5!, item6!, item7!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6, T7}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T1">元组第一个分量的类型。</typeparam>
    /// <typeparam name="T2">元组第二个分量的类型。</typeparam>
    /// <typeparam name="T3">元组第三个分量的类型。</typeparam>
    /// <typeparam name="T4">元组第四个分量的类型。</typeparam>
    /// <typeparam name="T5">元组第五个分量的类型。</typeparam>
    /// <typeparam name="T6">元组第六个分量的类型。</typeparam>
    /// <typeparam name="T7">元组第七个分量的类型。</typeparam>
    [RegisterCopier]
    public sealed class TupleCopier<T1, T2, T3, T4, T5, T6, T7> : IDeepCopier<Tuple<T1, T2, T3, T4, T5, T6, T7>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3, T4, T5, T6, T7>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;
        private readonly IDeepCopier<T7> _copier7;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3, T4, T5, T6, T7}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        /// <param name="copier7"><typeparamref name="T7"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3, T4, T5, T6, T7>? DeepCopy(Tuple<T1, T2, T3, T4, T5, T6, T7>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3, T4, T5, T6, T7>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = Tuple.Create(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4) ? input.Item4 : _copier4.DeepCopy(input.Item4, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5) ? input.Item5 : _copier5.DeepCopy(input.Item5, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6) ? input.Item6 : _copier6.DeepCopy(input.Item6, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier7) ? input.Item7 : _copier7.DeepCopy(input.Item7, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6, T7, T8}"/> 的序列化器。
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
    public sealed class TupleCodec<T1, T2, T3, T4, T5, T6, T7, T8> : IFieldCodec<Tuple<T1, T2, T3, T4, T5, T6, T7, T8>> where T8 : notnull
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
        /// 初始化 <see cref="TupleCodec{T1, T2, T3, T4, T5, T6, T7, T8}"/> 类的新实例。
        /// </summary>
        /// <param name="item1Codec"><typeparamref name="T1"/> 的编解码器。</param>
        /// <param name="item2Codec"><typeparamref name="T2"/> 的编解码器。</param>
        /// <param name="item3Codec"><typeparamref name="T3"/> 的编解码器。</param>
        /// <param name="item4Codec"><typeparamref name="T4"/> 的编解码器。</param>
        /// <param name="item5Codec"><typeparamref name="T5"/> 的编解码器。</param>
        /// <param name="item6Codec"><typeparamref name="T6"/> 的编解码器。</param>
        /// <param name="item7Codec"><typeparamref name="T7"/> 的编解码器。</param>
        /// <param name="item8Codec"><typeparamref name="T8"/> 的编解码器。</param>
        public TupleCodec(
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
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer,
            uint fieldIdDelta,
            [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType,
            [System.Diagnostics.CodeAnalysis.AllowNull] Tuple<T1, T2, T3, T4, T5, T6, T7, T8> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
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
        public Tuple<T1, T2, T3, T4, T5, T6, T7, T8> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Tuple<T1, T2, T3, T4, T5, T6, T7, T8>, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            var item1 = default(T1);
            var item2 = default(T2);
            var item3 = default(T3);
            var item4 = default(T4);
            var item5 = default(T5);
            var item6 = default(T6);
            var item7 = default(T7);
            var item8 = default(T8);
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
                        item1 = _item1Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        item2 = _item2Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        item3 = _item3Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        item4 = _item4Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        item5 = _item5Codec.ReadValue(ref reader, header);
                        break;
                    case 6:
                        item6 = _item6Codec.ReadValue(ref reader, header);
                        break;
                    case 7:
                        item7 = _item7Codec.ReadValue(ref reader, header);
                        break;
                    case 8:
                        item8 = _item8Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            var result = new Tuple<T1, T2, T3, T4, T5, T6, T7, T8>(item1!, item2!, item3!, item4!, item5!, item6!, item7!, item8!);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }
    }

    /// <summary>
    /// <see cref="Tuple{T1, T2, T3, T4, T5, T6, T7, T8}"/> 的复制器。
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
    public sealed class TupleCopier<T1, T2, T3, T4, T5, T6, T7, T8> : IDeepCopier<Tuple<T1, T2, T3, T4, T5, T6, T7, T8>>, IOptionalDeepCopier where T8 : notnull
    {
        private int _shallowCopyable;
        private readonly Type _fieldType = typeof(Tuple<T1, T2, T3, T4, T5, T6, T7, T8>);
        private readonly IDeepCopier<T1> _copier1;
        private readonly IDeepCopier<T2> _copier2;
        private readonly IDeepCopier<T3> _copier3;
        private readonly IDeepCopier<T4> _copier4;
        private readonly IDeepCopier<T5> _copier5;
        private readonly IDeepCopier<T6> _copier6;
        private readonly IDeepCopier<T7> _copier7;
        private readonly IDeepCopier<T8> _copier8;

        /// <summary>
        /// 初始化 <see cref="TupleCopier{T1, T2, T3, T4, T5, T6, T7, T8}"/> 类的新实例。
        /// </summary>
        /// <param name="copier1"><typeparamref name="T1"/> 的复制器。</param>
        /// <param name="copier2"><typeparamref name="T2"/> 的复制器。</param>
        /// <param name="copier3"><typeparamref name="T3"/> 的复制器。</param>
        /// <param name="copier4"><typeparamref name="T4"/> 的复制器。</param>
        /// <param name="copier5"><typeparamref name="T5"/> 的复制器。</param>
        /// <param name="copier6"><typeparamref name="T6"/> 的复制器。</param>
        /// <param name="copier7"><typeparamref name="T7"/> 的复制器。</param>
        /// <param name="copier8"><typeparamref name="T8"/> 的复制器。</param>
        public TupleCopier(
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

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public Tuple<T1, T2, T3, T4, T5, T6, T7, T8>? DeepCopy(Tuple<T1, T2, T3, T4, T5, T6, T7, T8>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy(input, out Tuple<T1, T2, T3, T4, T5, T6, T7, T8>? existing))
                return existing!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() as object != _fieldType as object)
                return context.DeepCopy(input);

            if (IsShallowCopyable())
                return input;

            // 如果输入元组中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var result = new Tuple<T1, T2, T3, T4, T5, T6, T7, T8>(
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier1) ? input.Item1 : _copier1.DeepCopy(input.Item1, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier2) ? input.Item2 : _copier2.DeepCopy(input.Item2, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier3) ? input.Item3 : _copier3.DeepCopy(input.Item3, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier4) ? input.Item4 : _copier4.DeepCopy(input.Item4, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier5) ? input.Item5 : _copier5.DeepCopy(input.Item5, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier6) ? input.Item6 : _copier6.DeepCopy(input.Item6, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier7) ? input.Item7 : _copier7.DeepCopy(input.Item7, context)!,
                OrleansGeneratedCodeHelper.IsShallowCopyable(_copier8) ? input.Rest : _copier8.DeepCopy(input.Rest, context)!);
            context.RecordCopy(input, result);
            return result;
        }
    }
}

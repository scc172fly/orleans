using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 一维数组的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ArrayCodec<T> : IFieldCodec<T[]>
    {
        private readonly IFieldCodec<T> _fieldCodec;
        private readonly Type CodecElementType = typeof(T);

        /// <summary>
        /// 初始化 <see cref="ArrayCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public ArrayCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] T[] value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

            if (value.Length > 0)
            {
                UInt32Codec.WriteField(ref writer, 0, (uint)value.Length);
                uint innerFieldIdDelta = 1;
                foreach (var element in value)
                {
                    _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                    innerFieldIdDelta = 0;
                }
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public T[] ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<T[], TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            T[]? result = null;
            uint fieldId = 0;
            var length = 0;
            var index = 0;
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
                        length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = new T[length];
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ThrowLengthFieldMissing();
                        }

                        if (index >= length)
                        {
                            ThrowIndexOutOfRangeException(length);
                        }

                        result![index] = _fieldCodec.ReadValue(ref reader, header)!;
                        ++index;
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            if (result is null)
            {
                result = Array.Empty<T>();
                ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            }

            return result!;
        }

        private static void ThrowIndexOutOfRangeException(int length) => throw new IndexOutOfRangeException(
            $"Encountered too many elements in array of type {typeof(T[])} with declared length {length}.");

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");
    }

    /// <summary>
    /// 一维数组的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ArrayCopier<T> : IDeepCopier<T[]>
    {
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="ArrayCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复制器。</param>
        public ArrayCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = OrleansGeneratedCodeHelper.UnwrapService(this, elementCopier);
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public T[]? DeepCopy(T[]? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<T[]>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            result = new T[input.Length];
            context.RecordCopy(input, result);
            for (var i = 0; i < input.Length; i++)
            {
                result[i] = _elementCopier.DeepCopy(input[i], context)!;
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="ReadOnlyMemory{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ReadOnlyMemoryCodec<T> : IFieldCodec<ReadOnlyMemory<T>>
    {
        private readonly Type CodecType = typeof(ReadOnlyMemory<T>);
        private readonly Type CodecElementType = typeof(T);
        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// 初始化 <see cref="ReadOnlyMemoryCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public ReadOnlyMemoryCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] ReadOnlyMemory<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!MemoryMarshal.TryGetArray(value, out var segment) || segment.Array!.Length != value.Length)
            {
                ReferenceCodec.MarkValueField(writer.Session);
            }
            else if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, CodecType, segment.Array))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecType, WireType.TagDelimited);

            UInt32Codec.WriteField(ref writer, 0, (uint)value.Length);
            uint innerFieldIdDelta = 1;
            foreach (var element in value.Span)
            {
                _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                innerFieldIdDelta = 0;
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public ReadOnlyMemory<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<T[], TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            T[]? result = null;
            uint fieldId = 0;
            var length = 0;
            var index = 0;
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
                        length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = new T[length];
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ThrowLengthFieldMissing();
                        }

                        if (index >= length)
                        {
                            ThrowIndexOutOfRangeException(length);
                        }

                        result![index] = _fieldCodec.ReadValue(ref reader, header)!;
                        ++index;
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return result!;
        }

        private static void ThrowIndexOutOfRangeException(int length) => throw new IndexOutOfRangeException(
            $"Encountered too many elements in array of type {typeof(T[])} with declared length {length}.");

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

    }

    /// <summary>
    /// <see cref="ReadOnlyMemory{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ReadOnlyMemoryCopier<T> : IDeepCopier<ReadOnlyMemory<T>>
    {
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="ReadOnlyMemoryCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复制器。</param>
        public ReadOnlyMemoryCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = OrleansGeneratedCodeHelper.UnwrapService(this, elementCopier);
        }

        /// <inheritdoc/>
        public ReadOnlyMemory<T> DeepCopy(ReadOnlyMemory<T> input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (input.IsEmpty)
            {
                return input;
            }

            var inputSpan = input.Span;
            var result = new T[inputSpan.Length];

            // 请注意，如果输入的底层对象能够参与循环引用，
            // 则可能发生无界递归。若能获取该对象，就可以阻止这种循环。
            // 底层对象也可能是 IMemoryOwner<T>，这种情况下该方法将无法奏效。
            if (MemoryMarshal.TryGetArray(input, out var segment) && segment.Array!.Length == result.Length)
            {
                if (context.TryGetCopy(segment.Array!, out T[]? existing))
                    return existing!;

                context.RecordCopy(segment.Array!, result);
            }

            for (var i = 0; i < inputSpan.Length; i++)
            {
                result[i] = _elementCopier.DeepCopy(inputSpan[i], context)!;
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="Memory{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class MemoryCodec<T> : IFieldCodec<Memory<T>>
    {
        private readonly Type CodecType = typeof(Memory<T>);
        private readonly Type CodecElementType = typeof(T);
        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// 初始化 <see cref="MemoryCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public MemoryCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Memory<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (!MemoryMarshal.TryGetArray<T>(value, out var segment) || segment.Array!.Length != value.Length)
            {
                ReferenceCodec.MarkValueField(writer.Session);
            }
            else if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, CodecType, segment.Array))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecType, WireType.TagDelimited);

            UInt32Codec.WriteField(ref writer, 0, (uint)value.Length);
            uint innerFieldIdDelta = 1;
            foreach (var element in value.Span)
            {
                _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                innerFieldIdDelta = 0;
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Memory<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<T[], TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            T[]? result = null;
            uint fieldId = 0;
            var length = 0;
            var index = 0;
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
                        length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = new T[length];
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ThrowLengthFieldMissing();
                        }

                        if (index >= length)
                        {
                            ThrowIndexOutOfRangeException(length);
                        }

                        result![index] = _fieldCodec.ReadValue(ref reader, header)!;
                        ++index;
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return result!;
        }

        private static void ThrowIndexOutOfRangeException(int length) => throw new IndexOutOfRangeException(
            $"Encountered too many elements in array of type {typeof(T[])} with declared length {length}.");

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

    }

    /// <summary>
    /// <see cref="Memory{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class MemoryCopier<T> : IDeepCopier<Memory<T>>
    {
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="MemoryCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复制器。</param>
        public MemoryCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = OrleansGeneratedCodeHelper.UnwrapService(this, elementCopier);
        }

        /// <inheritdoc/>
        public Memory<T> DeepCopy(Memory<T> input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (input.IsEmpty)
            {
                return input;
            }

            var inputSpan = input.Span;
            var result = new T[inputSpan.Length];

            // 请注意，如果输入的底层对象能够参与循环引用，
            // 则可能发生无界递归。若能获取该对象，就可以阻止这种循环。
            // 底层对象也可能是 IMemoryOwner<T>，这种情况下该方法将无法奏效。
            if (MemoryMarshal.TryGetArray<T>(input, out var segment) && segment.Array!.Length == result.Length)
            {
                if (context.TryGetCopy(segment.Array!, out T[]? existing))
                    return existing!;

                context.RecordCopy(segment.Array!, result);
            }

            for (var i = 0; i < inputSpan.Length; i++)
            {
                result[i] = _elementCopier.DeepCopy(inputSpan[i], context)!;
            }

            return result;
        }
    }

    /// <summary>
    /// <see cref="ArraySegment{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ArraySegmentCodec<T> : IFieldCodec<ArraySegment<T>>
    {
        private readonly Type CodecType = typeof(ArraySegment<T>);
        private readonly Type CodecElementType = typeof(T);
        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// 初始化 <see cref="ArraySegmentCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public ArraySegmentCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] ArraySegment<T> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value.Array?.Length != value.Count)
            {
                ReferenceCodec.MarkValueField(writer.Session);
            }
            else if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, CodecType, value.Array))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecType, WireType.TagDelimited);

            if (value.Count > 0)
            {
                UInt32Codec.WriteField(ref writer, 0, (uint)value.Count);
                uint innerFieldIdDelta = 1;
                foreach (var element in value.AsSpan())
                {
                    _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                    innerFieldIdDelta = 0;
                }
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public ArraySegment<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<T[], TInput>(ref reader, field)!;
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            T[]? result = null;
            uint fieldId = 0;
            var length = 0;
            var index = 0;
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
                        length = (int)UInt32Codec.ReadValue(ref reader, header);
                        reader.EnsureAvailable((uint)length);

                        result = new T[length];
                        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                        break;
                    case 1:
                        if (result is null)
                        {
                            ThrowLengthFieldMissing();
                        }

                        if (index >= length)
                        {
                            ThrowIndexOutOfRangeException(length);
                        }

                        result![index] = _fieldCodec.ReadValue(ref reader, header)!;
                        ++index;
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            return result!;
        }

        private static void ThrowIndexOutOfRangeException(int length) => throw new IndexOutOfRangeException(
            $"Encountered too many elements in array of type {typeof(T[])} with declared length {length}.");

        private static void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

    }

    /// <summary>
    /// <see cref="ArraySegment{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ArraySegmentCopier<T> : IDeepCopier<ArraySegment<T>>
    {
        private readonly IDeepCopier<T> _elementCopier;

        /// <summary>
        /// 初始化 <see cref="ArraySegmentCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="elementCopier">元素复制器。</param>
        public ArraySegmentCopier(IDeepCopier<T> elementCopier)
        {
            _elementCopier = OrleansGeneratedCodeHelper.UnwrapService(this, elementCopier);
        }

        /// <inheritdoc/>
        public ArraySegment<T> DeepCopy(ArraySegment<T> input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (input.Array is null)
            {
                return input;
            }

            var inputSpan = input.AsSpan();
            var result = new T[inputSpan.Length];
            context.RecordCopy(input.Array, result);
            for (var i = 0; i < inputSpan.Length; i++)
            {
                result[i] = _elementCopier.DeepCopy(inputSpan[i], context)!;
            }

            return new ArraySegment<T>(result);
        }
    }
}

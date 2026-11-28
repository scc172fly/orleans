using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Nullable{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">可空类型所包装的值类型。</typeparam>
    [RegisterSerializer]
    public sealed class NullableCodec<T> : IFieldCodec<T?> where T : struct
    {
        private readonly Type CodecFieldType = typeof(T);
        private readonly IFieldCodec<T> _fieldCodec;

        /// <summary>
        /// 初始化 <see cref="NullableCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="fieldCodec">字段编解码器。</param>
        public NullableCodec(IFieldCodec<T> fieldCodec)
        {
            _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] T? value) where TBufferWriter : IBufferWriter<byte>
        {
            // 如果值为 null，则将其作为 null 引用写入。
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            // 该值不为 null。
            _fieldCodec.WriteField(ref writer, fieldIdDelta, CodecFieldType, value.GetValueOrDefault());
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public T? ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            // 仅当值为 null 时，此条件才为 true。
            if (field.WireType == WireType.Reference)
            {
                ReferenceCodec.MarkValueField(reader.Session);
                var reference = reader.ReadVarUInt32();
                if (reference != 0) ThrowInvalidReference(reference);
                return null;
            }

            // 读取非 null 值。
            return _fieldCodec.ReadValue(ref reader, field);
        }

        private static void ThrowInvalidReference(uint reference) => throw new ReferenceNotFoundException(typeof(T?), reference);
    }

    /// <summary>
    /// <see cref="Nullable{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">可空类型所包装的值类型。</typeparam>
    [RegisterCopier]
    public sealed class NullableCopier<T> : IDeepCopier<T?>, IOptionalDeepCopier where T : struct
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="NullableCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">复制器。</param>
        public NullableCopier(IDeepCopier<T> copier)
            => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context) => input is null || IsShallowCopyable() ? input : _copier.DeepCopy(input, context);

        /// <inheritdoc/>
        public T? DeepCopy(T? input, CopyContext context) => input is null || IsShallowCopyable() ? input : _copier.DeepCopy(input.GetValueOrDefault(), context);
    }
}

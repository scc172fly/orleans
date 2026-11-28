using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 字段编解码器的标记类型。
    /// </summary>
    public interface IFieldCodec
    {
        /// <summary>
        /// 使用给定的无类型值写入字段。该值的实际类型仍必须与编解码器实例匹配！
        /// </summary>
        void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 读取值并以无类型形式返回。该值的实际类型仍必须与编解码器实例匹配！
        /// </summary>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? ReadValue<TInput>(ref Reader<TInput> reader, Field field);
    }

    /// <summary>
    /// 提供读写指定类型的值的功能。
    /// 实现 <see cref="Orleans.Serialization.Codecs.IFieldCodec" />。
    /// </summary>
    /// <typeparam name="T">此实现能够读写的类型。</typeparam>
    /// <seealso cref="Orleans.Serialization.Codecs.IFieldCodec" />
    public interface IFieldCodec<T> : IFieldCodec
    {
        /// <summary>
        /// 写入一个字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="value">值。</param>
        void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [AllowNull] Type expectedType, [AllowNull] T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: MaybeNull]
        new T ReadValue<TInput>(ref Reader<TInput> reader, Field field);

        void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value)
            => WriteField(ref writer, fieldIdDelta, expectedType, (T)value!);

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);
    }

    /// <summary>
    /// 标记接口，表示编解码器直接支持序列化指定类型的所有派生类型。
    /// </summary>
    public interface IDerivedTypeCodec : IFieldCodec
    {
    }

    /// <summary>
    /// 序列化及复制各阶段的挂钩。
    /// </summary>
    /// <typeparam name="T">底层值的类型。</typeparam>
    public interface ISerializationCallbacks<T>
    {
        /// <summary>
        /// 序列化时调用。
        /// </summary>
        /// <param name="value">值。</param>
        void OnSerializing(T value);

        /// <summary>
        /// 值序列化完成后调用。
        /// </summary>
        /// <param name="value">值。</param>
        void OnSerialized(T value);

        /// <summary>
        /// 反序列化时调用。
        /// </summary>
        /// <param name="value">值。</param>
        void OnDeserializing(T value);

        /// <summary>
        /// 值反序列化完成后调用。
        /// </summary>
        /// <param name="value">值。</param>
        void OnDeserialized(T value);

        /// <summary>
        /// 复制时调用。
        /// </summary>
        /// <param name="original">原始值。</param>
        /// <param name="result">副本。</param>
        void OnCopying(T original, T result);

        /// <summary>
        /// 值复制完成后调用。
        /// </summary>
        /// <param name="original">原始值。</param>
        /// <param name="result">副本。</param>
        void OnCopied(T original, T result);
    }

    internal sealed class UntypedCodecWrapper<TField> : IFieldCodec<TField>
    {
        private readonly IFieldCodec _codec;

        public UntypedCodecWrapper(IFieldCodec codec) => _codec = codec;

        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [AllowNull] Type expectedType, [AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
            => _codec.WriteField(ref writer, fieldIdDelta, expectedType, value);

        void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value)
            => _codec.WriteField(ref writer, fieldIdDelta, expectedType, value);

        [return: MaybeNull]
        public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field) => (TField)_codec.ReadValue(ref reader, field)!;

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => _codec.ReadValue(ref reader, field);
    }

}

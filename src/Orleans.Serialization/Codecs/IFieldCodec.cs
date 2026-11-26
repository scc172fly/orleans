using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 字段编码器的标记类型。
    /// </summary>
    public interface IFieldCodec
    {
        /// <summary>
        /// 使用提供的未输入值写入字段。该类型必须仍然匹配编码实例!
        /// </summary>
        void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 读取值并返回未输入的数值。 此类型必须仍然匹配编码实例!
        /// </summary>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? ReadValue<TInput>(ref Reader<TInput> reader, Field field);
    }

    /// <summary>
    /// 提供特定类型读写值的功能。
    /// 执行 <see cref="Orleans.Serialization.Codecs.IFieldCodec" />
    /// </summary>
    /// <typeparam name="T">此执行可读写的类型 。</typeparam>
    /// <seealso cref="Orleans.Serialization.Codecs.IFieldCodec" />
    public interface IFieldCodec<T> : IFieldCodec
    {
        /// <summary>
        /// 写一个字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="value">数值。</param>
        void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [AllowNull] Type expectedType, [AllowNull] T value) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// 读取值 。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>数值。</returns>
        [return: MaybeNull]
        new T ReadValue<TInput>(ref Reader<TInput> reader, Field field);

        void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value)
            => WriteField(ref writer, fieldIdDelta, expectedType, (T)value!);

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);
    }

    /// <summary>
    /// 直接支持所有衍生类型(指定类型)序列的编码器标记界面。
    /// </summary>
    public interface IDerivedTypeCodec : IFieldCodec
    {
    }

    /// <summary>
    /// 系列和复制阶段的钩子。
    /// </summary>
    /// <typeparam name="T">底值类型。</typeparam>
    public interface ISerializationCallbacks<T>
    {
        /// <summary>
        /// 连环处理时被调用
        /// </summary>
        /// <param name="value">数值。</param>
        void OnSerializing(T value);

        /// <summary>
        /// 某个值被序列化后调用。
        /// </summary>
        /// <param name="value">数值。</param>
        void OnSerialized(T value);

        /// <summary>
        /// 被召回时, 正在消毒。
        /// </summary>
        /// <param name="value">数值。</param>
        void OnDeserializing(T value);

        /// <summary>
        /// 当一个值被淡化后调用 。
        /// </summary>
        /// <param name="value">数值。</param>
        void OnDeserialized(T value);

        /// <summary>
        /// 复制时呼叫 。
        /// </summary>
        /// <param name="original">原始价值。</param>
        /// <param name="result">拷贝</param>
        void OnCopying(T original, T result);

        /// <summary>
        /// 复制一个值时调用 。
        /// </summary>
        /// <param name="original">原始价值。</param>
        /// <param name="result">拷贝</param>
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

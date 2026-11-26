using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 标记对象用于表示未知字段及其位置,将其显示为数据流。
    /// </summary>
    public sealed class UnknownFieldMarker
    {
        /// <summary>
        /// 初始化 <see cref="UnknownFieldMarker"/> 类的新实例。
        /// </summary>
        /// <param name="field">字段。</param>
        /// <param name="position">位置。</param>
        public UnknownFieldMarker(Field field, long position)
        {
            Field = field;
            Position = position;
        }

        /// <summary>
        /// 此字段所在的流中的位置。
        /// </summary>
        public long Position { get; }

        /// <summary>
        /// 字段信头 。
        /// </summary>
        public Field Field { get; }

        /// <inheritdoc />
        public override string ToString() => $"{nameof(Position)}: 0x{Position:X}, {nameof(Field)}: {Field}";

    }
}

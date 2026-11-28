using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 标记对象，用于指示未知字段及其在数据流中的位置。
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
        /// 此字段在流中出现的位置。
        /// </summary>
        public long Position { get; }

        /// <summary>
        /// 字段头。
        /// </summary>
        public Field Field { get; }

        /// <inheritdoc />
        public override string ToString() => $"{nameof(Position)}: 0x{Position:X}, {nameof(Field)}: {Field}";

    }
}

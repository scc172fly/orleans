using System;
using System.Collections.Generic;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 描述对 grain 状态版本的读数表示交易观察。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public struct Observation : IEquatable<Observation>
    {
        /// <summary>
        /// 获取或设置被观察的 grain的逻辑编号。
        /// </summary>
        [Id(0)]
        public int Grain { get; set; }

        /// <summary>
        /// 获取或设置观察状态版本序列编号。
        /// </summary>
        [Id(1)]
        public int SeqNo { get; set; }

        /// <summary>
        /// 获取或设置写入观察版本的交易标识符。
        /// </summary>
        [Id(2)]
        public string WriterTx { get; set; }

        /// <summary>
        /// 获取或设置进行观察的交易的识别符号。
        /// </summary>
        [Id(3)]
        public string ExecutingTx { get; set; }

        /// <inheritdoc/>
        public readonly bool Equals(Observation other) =>
            Grain == other.Grain
            && SeqNo == other.SeqNo
            && EqualityComparer<string>.Default.Equals(WriterTx, other.WriterTx)
            && EqualityComparer<string>.Default.Equals(ExecutingTx, other.ExecutingTx);

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj) => obj is Observation other && Equals(other);

        /// <inheritdoc/>
        public override readonly int GetHashCode() => HashCode.Combine(Grain, SeqNo, WriterTx, ExecutingTx);

        /// <summary>
        /// 确定两个观察是否相等。
        /// </summary>
        public static bool operator ==(Observation left, Observation right) => left.Equals(right);

        /// <summary>
        /// 确定两种意见是否不平等。
        /// </summary>
        public static bool operator !=(Observation left, Observation right) => !left.Equals(right);
    }
}

using System;
using System.Collections.Generic;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 描述一次事务读取所观测到的 Grain 状态版本。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public struct Observation : IEquatable<Observation>
    {
        /// <summary>
        /// 获取或设置被观测 Grain 的逻辑编号。
        /// </summary>
        [Id(0)]
        public int Grain { get; set; }

        /// <summary>
        /// 获取或设置观测到的状态版本序列号。
        /// </summary>
        [Id(1)]
        public int SeqNo { get; set; }

        /// <summary>
        /// 获取或设置写入观测版本的事务标识符。
        /// </summary>
        [Id(2)]
        public string WriterTx { get; set; }

        /// <summary>
        /// 获取或设置执行此次观测的事务标识符。
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
        /// 确定两条观测记录是否相等。
        /// </summary>
        public static bool operator ==(Observation left, Observation right) => left.Equals(right);

        /// <summary>
        /// 确定两条观测记录是否不相等。
        /// </summary>
        public static bool operator !=(Observation left, Observation right) => !left.Equals(right);
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit.Correctnesss
{
    /// <summary>
    /// 表示以紧凑的 32 位整数值存储的可扩展位数组。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class BitArrayState
    {
        /// <summary>
        /// 确定此实例与另一个实例是否包含相同的紧凑存储位值。
        /// </summary>
        /// <param name="other">要与此实例比较的实例。</param>
        /// <returns><see langword="true"/> 表示紧凑存储值相等；否则为 <see langword="false"/>。</returns>
        protected bool Equals(BitArrayState other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (ReferenceEquals(null, this.value)) return false;
            if (ReferenceEquals(null, other.value)) return false;
            if (this.value.Length != other.value.Length) return false;
            for (var i = 0; i < this.value.Length; i++)
            {
                if (this.value[i] != other.value[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((BitArrayState)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(value);

        private static readonly int BitsInInt = sizeof(int) * 8;

        [JsonProperty("v")]
        [Id(0)]
        private int[] value = { 0 };

        /// <summary>
        /// 获取存储这些位的紧凑 32 位值。
        /// </summary>
        [JsonIgnore]
        public int[] Value => value;

        /// <summary>
        /// 获取状态中紧凑 32 位值的数量。
        /// </summary>
        [JsonIgnore]
        public int Length => this.value.Length;

        /// <summary>
        /// 初始化 <see cref="BitArrayState"/> 类的新实例，并清零所有位。
        /// </summary>
        public BitArrayState()
        {
        }

        /// <summary>
        /// 通过复制另一个实例来初始化 <see cref="BitArrayState"/> 类的新实例。
        /// </summary>
        /// <param name="other">要复制的状态。</param>
        public BitArrayState(BitArrayState other)
        {
            ArgumentNullException.ThrowIfNull(other);
            this.value = new int[other.value.Length];
            for (var i = 0; i < other.value.Length; i++)
            {
                this.value[i] = other.value[i];
            }
        }

        /// <summary>
        /// 设置或清除指定的位，并在必要时扩展底层存储。
        /// </summary>
        /// <param name="index">位索引（从零开始）。</param>
        /// <param name="value"><see langword="true"/> 表示设置该位；<see langword="false"/> 表示清除该位。</param>
        public void Set(int index, bool value)
        {
            int idx = index / BitsInInt;
            if (idx >= this.value.Length)
            {
                Array.Resize(ref this.value, idx + 1);
            }
            int shift = 1 << (index % BitsInInt);
            if (value)
            {
                this.value[idx] |= shift;
            }
            else
                this.value[idx] &= ~shift;
        }

        /// <summary>
        /// 返回用于遍历紧凑 32 位值的枚举器。
        /// </summary>
        /// <returns>用于遍历底层存储值的枚举器。</returns>
        public IEnumerator<int> GetEnumerator()
        {
            foreach (var v in this.value) yield return v;
        }

        /// <summary>
        /// 将各紧凑存储值中的位按从最低有效位到最高有效位的顺序排列，以字符串形式返回。
        /// </summary>
        /// <returns>状态的二进制表示。</returns>
        public override string ToString()
        {
            // 按从最低有效位到最高有效位的顺序写入值
            var builder = new StringBuilder();
            foreach (var v in this.value)
            {
                builder.Append(Reverse(Convert.ToString(v, 2)).PadRight(BitsInInt, '0'));

                string Reverse(string s)
                {
                    char[] charArray = s.ToCharArray();
                    Array.Reverse(charArray);
                    return new string(charArray);
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// 按存储索引获取或设置紧凑的 32 位值。
        /// </summary>
        /// <param name="index">存储索引（从零开始）。</param>
        /// <value><paramref name="index"/> 处的紧凑存储值。</value>
        public int this[int index]
        {
            get => this.value[index];
            set => this.value[index] = value;
        }

        /// <summary>
        /// 确定两个实例是否包含相同的紧凑存储位值。
        /// </summary>
        /// <param name="left">要比较的第一个实例。</param>
        /// <param name="right">要比较的第二个实例。</param>
        /// <returns><see langword="true"/> 表示两个实例相等；否则为 <see langword="false"/>。</returns>
        public static bool operator ==(BitArrayState? left, BitArrayState? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null)) return false;
            if (ReferenceEquals(right, null)) return false;
            return left.Equals(right);
        }

        /// <summary>
        /// 确定两个实例是否包含不同的紧凑存储位值。
        /// </summary>
        /// <param name="left">要比较的第一个实例。</param>
        /// <param name="right">要比较的第二个实例。</param>
        /// <returns><see langword="true"/> 表示两个实例不同；否则为 <see langword="false"/>。</returns>
        public static bool operator !=(BitArrayState? left, BitArrayState? right)
        {
            return !(left == right);
        }

        /// <summary>
        /// 计算两个状态的按位异或。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>包含按位异或结果的新状态。</returns>
        public static BitArrayState operator ^(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l ^ r);
        }

        /// <summary>
        /// 计算两个状态的按位或。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>包含按位或结果的新状态。</returns>
        public static BitArrayState operator |(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l | r);
        }

        /// <summary>
        /// 计算两个状态的按位与。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>包含按位与结果的新状态。</returns>
        public static BitArrayState operator &(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l & r);
        }

        /// <summary>
        /// 对两个状态中对应的紧凑存储值执行二元运算。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <param name="op">应用于每对紧凑存储值的运算。</param>
        /// <returns>包含运算结果的新状态。</returns>
        public static BitArrayState Apply(BitArrayState left, BitArrayState right, Func<int, int, int> op)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            ArgumentNullException.ThrowIfNull(op);

            var result = new BitArrayState(left.value.Length > right.value.Length ? left : right);
            var overlappingLength = Math.Min(left.value.Length, right.value.Length);
            var i = 0;
            for (; i < overlappingLength; i++)
            {
                result.value[i] = op(left.value[i], right.value[i]);
            }

            // 继续处理不重叠的部分。
            for (; i < result.value.Length; i++)
            {
                var leftVal = left.value.Length > i ? left.value[i] : 0;
                var rightVal = right.value.Length > i ? right.value[i] : 0;
                result.value[i] = op(leftVal, rightVal);
            }

            return result;
        }
    }

    /// <summary>
    /// 在支持的最大数量的协同事务状态上实现位数组事务测试操作。
    /// </summary>
    [GrainType("txn-correctness-MaxStateTransactionalGrain")]
    public class MaxStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="MaxStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="stateFactory">用于创建事务状态的工厂。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public MaxStateTransactionalGrain(ITransactionalStateFactory stateFactory,
            ILoggerFactory loggerFactory)
            : base(Enumerable.Range(0, TransactionTestConstants.MaxCoordinatedTransactions)
                .Select(i => stateFactory.Create<BitArrayState>(new TransactionalStateConfiguration(new TransactionalStateAttribute($"data{i}", TransactionTestConstants.TransactionStore))))
                .ToArray(),
                  loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在两个事务状态上实现位数组事务测试操作。
    /// </summary>
    [GrainType("txn-correctness-DoubleStateTransactionalGrain")]
    public class DoubleStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="DoubleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data1">第一个事务状态。</param>
        /// <param name="data2">第二个事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public DoubleStateTransactionalGrain(
            [TransactionalState("data1", TransactionTestConstants.TransactionStore)]
            ITransactionalState<BitArrayState> data1,
            [TransactionalState("data2", TransactionTestConstants.TransactionStore)]
            ITransactionalState<BitArrayState> data2,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<BitArrayState>[2] { data1, data2 }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在一个事务状态上实现位数组事务测试操作。
    /// </summary>
    [GrainType("txn-correctness-SingleStateTransactionalGrain")]
    public class SingleStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="SingleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public SingleStateTransactionalGrain(
            [TransactionalState("data", TransactionTestConstants.TransactionStore)]
            ITransactionalState<BitArrayState> data,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<BitArrayState>[1] { data }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在有序的位数组状态集合上提供事务测试操作。
    /// </summary>
    [GrainType("txn-correctness-MultiStateTransactionalBitArrayGrain")]
    public partial class MultiStateTransactionalBitArrayGrain : Grain, ITransactionalBitArrayGrain
    {
        /// <summary>
        /// 此 Grain 操作的事务状态。
        /// </summary>
        protected ITransactionalState<BitArrayState>[] dataArray;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 此 Grain 激活实例的日志记录器。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="MultiStateTransactionalBitArrayGrain"/> 类的新实例。
        /// </summary>
        /// <param name="dataArray">有序的事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public MultiStateTransactionalBitArrayGrain(
            ITransactionalState<BitArrayState>[] dataArray,
            ILoggerFactory loggerFactory)
        {
            this.dataArray = dataArray;
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger = this.loggerFactory.CreateLogger(this.GetGrainId().ToString());
            LogTraceGrainId(this.logger, this.GetPrimaryKey());

            return base.OnActivateAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task Ping()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetBit(int index)
        {
            return Task.WhenAll(this.dataArray
                .Select(data => data.PerformUpdate(state =>
                {
                    LogTraceSettingBit(this.logger, index, state, TransactionContext.CurrentTransactionId);
                    state.Set(index, true);
                    LogTraceSetBit(this.logger, index, state);
                })));
        }

        /// <inheritdoc/>
        public async Task<List<BitArrayState>> Get()
        {
            return (await Task.WhenAll(this.dataArray
                .Select(state => state.PerformRead(s =>
                {
                    LogTraceGetState(this.logger, s);
                    return s;
                })))).ToList();
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "GrainId: {GrainId}."
        )]
        private static partial void LogTraceGrainId(ILogger logger, Guid grainId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Setting bit {Index} in state {State}. Transaction {CurrentTransactionId}"
        )]
        private static partial void LogTraceSettingBit(ILogger logger, int index, BitArrayState state, object currentTransactionId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Set bit {Index} in state {State}."
        )]
        private static partial void LogTraceSetBit(ILogger logger, int index, BitArrayState state);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Get state {State}."
        )]
        private static partial void LogTraceGetState(ILogger logger, BitArrayState state);
    }
}

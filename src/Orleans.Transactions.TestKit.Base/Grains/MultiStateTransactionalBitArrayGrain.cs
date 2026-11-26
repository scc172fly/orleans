
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
    /// 表示以包装 32 位整数值存储的可增殖比特阵列。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class BitArrayState
    {
        /// <summary>
        /// 确定此例是否包含与其它实例相同的已打包位值。
        /// </summary>
        /// <param name="other">与此实例比较的示例。</param>
        /// <returns><see langword="true"/> 当包装值相等时;否则, <see langword="false"/>.</returns>
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
        /// 获得存储比特的32位数的包装值。
        /// </summary>
        [JsonIgnore]
        public int[] Value => value;

        /// <summary>
        /// 获得该州内32位值的包装数。
        /// </summary>
        [JsonIgnore]
        public int Length => this.value.Length;

        /// <summary>
        /// 初始化 <see cref="BitArrayState"/> 类的新实例, 并清除所有位数 。
        /// </summary>
        public BitArrayState()
        {
        }

        /// <summary>
        /// 通过复制另一个实例, 初始化 <see cref="BitArrayState"/> 类的新实例 。
        /// </summary>
        /// <param name="other">国家复制。</param>
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
        /// 设置或清理一点, 必要时会增加备份存储 。
        /// </summary>
        /// <param name="index">零位指数。</param>
        /// <param name="value"><see langword="true"/> 设置比特; <see langword="false"/> 清除它。</param>
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
        /// 返回32位数值以上的一个数值。
        /// </summary>
        /// <returns>背值之上的分子。</returns>
        public IEnumerator<int> GetEnumerator()
        {
            foreach (var v in this.value) yield return v;
        }

        /// <summary>
        /// 返回每个包装值中从最小值到最显著值的字符串中的比特。
        /// </summary>
        /// <returns>国家二进制代表</returns>
        public override string ToString()
        {
            // 将最小比特的值写入最显著比特
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
        /// 以存储指数来计算或设置一个包装的32位数值。
        /// </summary>
        /// <param name="index">零基储存指数。</param>
        /// <value>包装价值 <paramref name="index"/>.</value>
        public int this[int index]
        {
            get => this.value[index];
            set => this.value[index] = value;
        }

        /// <summary>
        /// 确定两个实例是否包含相同的打包位数值。
        /// </summary>
        /// <param name="left">比较的首例。</param>
        /// <param name="right">比较二审</param>
        /// <returns><see langword="true"/> 等情况相等时;否则, <see langword="false"/>。</returns>
        public static bool operator ==(BitArrayState? left, BitArrayState? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null)) return false;
            if (ReferenceEquals(right, null)) return false;
            return left.Equals(right);
        }

        /// <summary>
        /// 确定两个实例是否包含不同的包装位数值。
        /// </summary>
        /// <param name="left">比较的首例。</param>
        /// <param name="right">比较二审</param>
        /// <returns><see langword="true"/> ,但情况不同;否则, <see langword="false"/>。</returns>
        public static bool operator !=(BitArrayState? left, BitArrayState? right)
        {
            return !(left == right);
        }

        /// <summary>
        /// 计算两个州中 比比奇的独家或专有性。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>含有比位数独家 OR的新状态。</returns>
        public static BitArrayState operator ^(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l ^ r);
        }

        /// <summary>
        /// 计算两个州的比特数或值。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>含有比位 OR的新状态。</returns>
        public static BitArrayState operator |(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l | r);
        }

        /// <summary>
        /// 计算两个州的比特数和值。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <returns>含有比特和的新状态。</returns>
        public static BitArrayState operator &(BitArrayState left, BitArrayState right)
        {
            return Apply(left, right, (l, r) => l & r);
        }

        /// <summary>
        /// 对两个州相应的包装值应用二进制操作。
        /// </summary>
        /// <param name="left">第一个状态。</param>
        /// <param name="right">第二个状态。</param>
        /// <param name="op">操作适用于每对包装的数值。</param>
        /// <returns>含有操作结果的新状态。</returns>
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

            // 继续使用不重叠的部分 。
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
    /// 在最大支持的协调州数范围内实施比特数交易测试操作。
    /// </summary>
    [GrainType("txn-correctness-MaxStateTransactionalGrain")]
    public class MaxStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="MaxStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="stateFactory">工厂用来建立交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
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
    /// 在两个交易州实施比特数交易测试操作。
    /// </summary>
    [GrainType("txn-correctness-DoubleStateTransactionalGrain")]
    public class DoubleStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="DoubleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data1">第一个交易状态。</param>
        /// <param name="data2">第二个交易状态。</param>
        /// <param name="loggerFactory">伐木厂</param>
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
    /// 在一个交易状态下实施比特数数交易测试操作。
    /// </summary>
    [GrainType("txn-correctness-SingleStateTransactionalGrain")]
    public class SingleStateTransactionalGrain : MultiStateTransactionalBitArrayGrain
    {
        /// <summary>
        /// 初始化 <see cref="SingleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
        public SingleStateTransactionalGrain(
            [TransactionalState("data", TransactionTestConstants.TransactionStore)]
            ITransactionalState<BitArrayState> data,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<BitArrayState>[1] { data }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 提供对一连串比特数州进行定购交易测试的业务。
    /// </summary>
    [GrainType("txn-correctness-MultiStateTransactionalBitArrayGrain")]
    public partial class MultiStateTransactionalBitArrayGrain : Grain, ITransactionalBitArrayGrain
    {
        /// <summary>
        /// 交易国以该 grain为业务基础。
        /// </summary>
        protected ITransactionalState<BitArrayState>[] dataArray;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 启动 grain 的登录器 。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="MultiStateTransactionalBitArrayGrain"/> 类的新实例。
        /// </summary>
        /// <param name="dataArray">被命令交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
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

using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;
using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 表示存储在交易状态 grain 中的整数。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GrainData
    {
        /// <summary>
        /// 获取或设置存储值。
        /// </summary>
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 执行超过最大支助数目的协调州的交易测试业务。
    /// </summary>
    public class MaxStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="MaxStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="stateFactory">工厂用来建立交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
        public MaxStateTransactionalGrain(ITransactionalStateFactory stateFactory,
            ILoggerFactory loggerFactory)
            : base(Enumerable.Range(0, TransactionTestConstants.MaxCoordinatedTransactions)
                .Select(i => stateFactory.Create<GrainData>(new TransactionalStateConfiguration(new TransactionalStateAttribute($"data{i}", TransactionTestConstants.TransactionStore))))
                .ToArray(),
                  loggerFactory)
        {
        }
    }

    /// <summary>
    /// 执行两个交易州的交易测试业务。
    /// </summary>
    public class DoubleStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="DoubleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data1">第一个交易状态。</param>
        /// <param name="data2">第二个交易状态。</param>
        /// <param name="loggerFactory">伐木厂</param>
        public DoubleStateTransactionalGrain(
            [TransactionalState("data1", TransactionTestConstants.TransactionStore)]
            ITransactionalState<GrainData> data1,
            [TransactionalState("data2", TransactionTestConstants.TransactionStore)]
            ITransactionalState<GrainData> data2,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<GrainData>[2] { data1, data2 }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在一个交易状态下实施交易测试业务。
    /// </summary>
    public class SingleStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="SingleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
        public SingleStateTransactionalGrain(
            [TransactionalState("data", TransactionTestConstants.TransactionStore)]
            ITransactionalState<GrainData> data,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<GrainData>[1] { data }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 实施交易测试业务,没有交易状态。
    /// </summary>
    public class NoStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="NoStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="loggerFactory">伐木厂</param>
        public NoStateTransactionalGrain(
            ILoggerFactory loggerFactory)
            : base(Array.Empty<ITransactionalState<GrainData>>(), loggerFactory)
        {
        }
    }

    /// <summary>
    /// 提供对整数状态的定单收集进行交易测试的业务。
    /// </summary>
    public partial class MultiStateTransactionalGrainBaseClass : Grain, ITransactionTestGrain
    {
        /// <summary>
        /// 交易国以该 grain为业务基础。
        /// </summary>
        protected ITransactionalState<GrainData>[] dataArray;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 启动 grain 的登录器 。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="MultiStateTransactionalGrainBaseClass"/> 类的新实例。
        /// </summary>
        /// <param name="dataArray">被命令交易状态</param>
        /// <param name="loggerFactory">伐木厂</param>
        public MultiStateTransactionalGrainBaseClass(
            ITransactionalState<GrainData>[] dataArray,
            ILoggerFactory loggerFactory)
        {
            this.dataArray = dataArray;
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc/>
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger = this.loggerFactory.CreateLogger(this.GetGrainId().ToString());
            return base.OnActivateAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task Set(int newValue)
        {
            foreach (var data in this.dataArray)
            {
                await data.PerformUpdate(state =>
                {
                    LogInformationSettingValue(this.logger, state.Value, newValue);
                    state.Value = newValue;
                    LogInformationSetValue(this.logger, state.Value);
                });
            }
        }

        /// <inheritdoc/>
        public async Task<int[]> Add(int numberToAdd)
        {
            var result = new int[dataArray.Length];
            for (int i = 0; i < dataArray.Length; i++)
            {
                result[i] = await dataArray[i].PerformUpdate(state =>
                {
                    LogInformationAddingValue(this.logger, numberToAdd, state.Value);
                    state.Value += numberToAdd;
                    LogInformationValueAfterAdd(this.logger, numberToAdd, state.Value);
                    return state.Value;
                });
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<int[]> Get()
        {
            var result = new int[dataArray.Length];
            for (int i = 0; i < dataArray.Length; i++)
            {
                result[i] = await dataArray[i].PerformRead(state =>
                {
                    LogInformationGetValue(this.logger, state.Value);
                    return state.Value;
                });
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task<int[]> GetCommitted()
        {
            var result = new int[dataArray.Length];
            for (int i = 0; i < dataArray.Length; i++)
            {
                result[i] = await dataArray[i].PerformRead(state => state.Value, ReadIsolation.ReadCommitted);
            }
            return result;
        }

        /// <inheritdoc/>
        public async Task AddAndThrow(int numberToAdd)
        {
            await Add(numberToAdd);
            throw new AddAndThrowException($"{GetType().Name} test exception");
        }

        /// <inheritdoc/>
        public async Task SetAndThrow(int numberToSet)
        {
            await Set(numberToSet);
            throw new AddAndThrowException($"{GetType().Name} test exception");
        }

        /// <inheritdoc/>
        public Task Deactivate()
        {
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Setting from {Value} to {NewValue}."
        )]
        private static partial void LogInformationSettingValue(ILogger logger, int value, int newValue);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Set to {Value}."
        )]
        private static partial void LogInformationSetValue(ILogger logger, int value);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Adding {NumberToAdd} to value {Value}."
        )]
        private static partial void LogInformationAddingValue(ILogger logger, int numberToAdd, int value);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Value after Adding {NumberToAdd} is {Value}."
        )]
        private static partial void LogInformationValueAfterAdd(ILogger logger, int numberToAdd, int value);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Get {Value}."
        )]
        private static partial void LogInformationGetValue(ILogger logger, int value);
    }

    /// <summary>
    /// 表示交易状态更新后故意造成的失败。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class AddAndThrowException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        public AddAndThrowException() : base("Unexpected error.") { }

        /// <summary>
        /// 初始化有消息的 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述失败的电文。</param>
        public AddAndThrowException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="AddAndThrowException"/> 类的新实例,其中含有一条消息和内置例外。
        /// </summary>
        /// <param name="message">描述失败的电文。</param>
        /// <param name="innerException">造成这一失败的例外。</param>
        public AddAndThrowException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 从序列数据初始化了 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列的例外数据。</param>
        /// <param name="context">序列化背景。</param>
        [Obsolete]
        protected AddAndThrowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

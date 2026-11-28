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
    /// 表示存储在事务 Grain 状态中的整数值。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GrainData
    {
        /// <summary>
        /// 获取或设置存储的值。
        /// </summary>
        [Id(0)]
        public int Value { get; set; }
    }

    /// <summary>
    /// 在支持的最大数量的协同事务状态上实现事务测试操作。
    /// </summary>
    public class MaxStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="MaxStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="stateFactory">用于创建事务状态的工厂。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
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
    /// 在两个事务状态上实现事务测试操作。
    /// </summary>
    public class DoubleStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="DoubleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data1">第一个事务状态。</param>
        /// <param name="data2">第二个事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
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
    /// 在一个事务状态上实现事务测试操作。
    /// </summary>
    public class SingleStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="SingleStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public SingleStateTransactionalGrain(
            [TransactionalState("data", TransactionTestConstants.TransactionStore)]
            ITransactionalState<GrainData> data,
            ILoggerFactory loggerFactory)
            : base(new ITransactionalState<GrainData>[1] { data }, loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在无事务状态的情况下实现事务测试操作。
    /// </summary>
    public class NoStateTransactionalGrain : MultiStateTransactionalGrainBaseClass
    {
        /// <summary>
        /// 初始化 <see cref="NoStateTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public NoStateTransactionalGrain(
            ILoggerFactory loggerFactory)
            : base(Array.Empty<ITransactionalState<GrainData>>(), loggerFactory)
        {
        }
    }

    /// <summary>
    /// 在有序的整数状态集合上提供事务测试操作。
    /// </summary>
    public partial class MultiStateTransactionalGrainBaseClass : Grain, ITransactionTestGrain
    {
        /// <summary>
        /// 此 Grain 操作的事务状态。
        /// </summary>
        protected ITransactionalState<GrainData>[] dataArray;
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// 此 Grain 激活实例的日志记录器。
        /// </summary>
        protected ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="MultiStateTransactionalGrainBaseClass"/> 类的新实例。
        /// </summary>
        /// <param name="dataArray">有序的事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
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
    /// 表示在更新事务状态之后有意引发的失败。
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
        /// 使用消息初始化 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述失败的消息。</param>
        public AddAndThrowException(string message) : base(message) { }

        /// <summary>
        /// 使用消息和内部异常初始化 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述失败的消息。</param>
        /// <param name="innerException">导致此失败的异常。</param>
        public AddAndThrowException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 从序列化数据初始化 <see cref="AddAndThrowException"/> 类的新实例。
        /// </summary>
        /// <param name="info">已序列化的异常数据。</param>
        /// <param name="context">序列化上下文。</param>
        [Obsolete]
        protected AddAndThrowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}

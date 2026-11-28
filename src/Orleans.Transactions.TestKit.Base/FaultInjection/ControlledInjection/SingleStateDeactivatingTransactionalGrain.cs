using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{

    /// <summary>
    /// 定义用于测试受控故障注入的事务操作。
    /// </summary>
    public interface IFaultInjectionTransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 在事务中设置 Grain 的值。
        /// </summary>
        /// <param name="newValue">新值。</param>
        /// <returns>表示该操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 在事务中增加 Grain 的值并配置故障注入。
        /// </summary>
        /// <param name="numberToAdd">要增加的数值。</param>
        /// <param name="faultInjectionControl">要注入的故障；指定 <see langword="null"/> 则不注入受控故障。</param>
        /// <returns>表示该操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Add(int numberToAdd, FaultInjectionControl? faultInjectionControl = null);

        /// <summary>
        /// 在事务中获取 Grain 的值。
        /// </summary>
        /// <returns>Grain 的值。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int> Get();

        /// <summary>
        /// 调度 Grain 的停用操作。
        /// </summary>
        /// <returns>已完成的任务。</returns>
        Task Deactivate();
    }

    /// <summary>
    /// 实现用于测试受控故障注入的事务状态操作。
    /// </summary>
    public partial class SingleStateFaultInjectionTransactionalGrain : Grain, IFaultInjectionTransactionTestGrain
    {
        private readonly IFaultInjectionTransactionalState<GrainData> data;
        private readonly ILoggerFactory loggerFactory;
        private ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="SingleStateFaultInjectionTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">支持故障注入的事务状态。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public SingleStateFaultInjectionTransactionalGrain(
            [FaultInjectionTransactionalState("data", TransactionTestConstants.TransactionStore)]
            IFaultInjectionTransactionalState<GrainData> data,
            ILoggerFactory loggerFactory)
        {
            this.data = data;
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.logger = this.loggerFactory.CreateLogger(this.GetGrainId().ToString());
            LogInformationGrainId(this.logger, this.GetPrimaryKey());

            return base.OnActivateAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task Set(int newValue)
        {
            return this.data.PerformUpdate(d =>
            {
                LogInformationSettingValue(this.logger, newValue);
                d.Value = newValue;
            });
        }

        /// <inheritdoc />
        public Task Add(int numberToAdd, FaultInjectionControl? faultInjectionControl = null)
        {
            // 进行重置，以防上一个事务的控制配置因某种原因未清除
            this.data.FaultInjectionControl.Reset();
            // 不要用 this.data.FaultInjectionControl = faultInjectionControl 替换它，
            // this.data.FaultInjectionControl 必须保持为同一个引用
            if (faultInjectionControl != null)
            {
                this.data.FaultInjectionControl.FaultInjectionPhase = faultInjectionControl.FaultInjectionPhase;
                this.data.FaultInjectionControl.FaultInjectionType = faultInjectionControl.FaultInjectionType;
            }

            return this.data.PerformUpdate(d =>
            {
                LogInformationAddingValue(this.logger, numberToAdd, d.Value);
                d.Value += numberToAdd;
            });
        }

        /// <inheritdoc />
        public Task<int> Get()
        {
            return this.data.PerformRead<int>(d => d.Value);
        }

        /// <inheritdoc />
        public Task Deactivate()
        {
            this.DeactivateOnIdle();
            return Task.CompletedTask;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "GrainId {GrainId}"
        )]
        private static partial void LogInformationGrainId(ILogger logger, Guid grainId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Setting value {NewValue}."
        )]
        private static partial void LogInformationSettingValue(ILogger logger, int newValue);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Adding {NumberToAdd} to value {Value}."
        )]
        private static partial void LogInformationAddingValue(ILogger logger, int numberToAdd, int value);
    }
}

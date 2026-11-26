using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{

    /// <summary>
    /// 定义用于测试受控过失注入的往来业务。
    /// </summary>
    public interface IFaultInjectionTransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 设置交易中的 grain 值。
        /// </summary>
        /// <param name="newValue">新值。</param>
        /// <returns>代表操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 在交易中添加 grain 值,并配置错误注入。
        /// </summary>
        /// <param name="numberToAdd">增加值。</param>
        /// <param name="faultInjectionControl">注射的过错,或 <see langword="null"/> 来进行操作,无受控过失。</param>
        /// <returns>代表操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Add(int numberToAdd, FaultInjectionControl? faultInjectionControl = null);

        /// <summary>
        /// 获取交易中的 grain 值。
        /// </summary>
        /// <returns>数值 grain 。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int> Get();

        /// <summary>
        /// 将 grain 排在停用时间表上。
        /// </summary>
        /// <returns>一项已完成的任务。</returns>
        Task Deactivate();
    }

    /// <summary>
    /// 实施用于测试受控过失注射的州交易业务。
    /// </summary>
    public partial class SingleStateFaultInjectionTransactionalGrain : Grain, IFaultInjectionTransactionTestGrain
    {
        private readonly IFaultInjectionTransactionalState<GrainData> data;
        private readonly ILoggerFactory loggerFactory;
        private ILogger logger = null!;

        /// <summary>
        /// 初始化 <see cref="SingleStateFaultInjectionTransactionalGrain"/> 类的新实例。
        /// </summary>
        /// <param name="data">错误输入交易状态。</param>
        /// <param name="loggerFactory">伐木厂</param>
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
            //重置, 以防由于某种原因无法清除上一个 tx 的控制
            this.data.FaultInjectionControl.Reset();
            //不以 this.data.FaultInjectionControl = faultInjectionControl替换, 
            //this.data.FaultInjectionControl 必须保持相同的提及
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

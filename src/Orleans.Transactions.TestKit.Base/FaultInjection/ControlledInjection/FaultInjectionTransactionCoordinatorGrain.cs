using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 协调多个故障注入测试 Grain 之间的事务操作。
    /// </summary>
    public interface IFaultInjectionTransactionCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 在新事务中设置每个 Grain 的值。
        /// </summary>
        /// <param name="grains">要更新的 Grain。</param>
        /// <param name="numberToAdd">要为每个 Grain 设置的值。</param>
        /// <returns>表示该操作的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSet(List<IFaultInjectionTransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新事务中为每个 Grain 的值增加指定数值，并配置故障注入。
        /// </summary>
        /// <param name="grains">要更新的 Grain。</param>
        /// <param name="numberToAdd">要为每个 Grain 增加的数值。</param>
        /// <param name="faultInjection">要注入的故障；指定 <see langword="null"/> 则不注入受控故障。</param>
        /// <returns>表示该操作的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAddAndFaultInjection(List<IFaultInjectionTransactionTestGrain> grains, int numberToAdd,
            FaultInjectionControl? faultInjection = null);
    }

    /// <summary>
    /// 协调多个故障注入测试 Grain 之间的事务操作。
    /// </summary>
    public class FaultInjectionTransactionCoordinatorGrain : Grain, IFaultInjectionTransactionCoordinatorGrain
    {
        /// <inheritdoc />
        public Task MultiGrainSet(List<IFaultInjectionTransactionTestGrain> grains, int newValue)
        {
            return Task.WhenAll(grains.Select(g => g.Set(newValue)));
        }

        /// <inheritdoc />
        public Task MultiGrainAddAndFaultInjection(List<IFaultInjectionTransactionTestGrain> grains, int numberToAdd,
            FaultInjectionControl? faultInjection = null)
        {
            return Task.WhenAll(grains.Select(g => g.Add(numberToAdd, faultInjection)));
        }
    }
}

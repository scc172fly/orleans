using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 坐标为错误注射试验 grains的往来业务。
    /// </summary>
    public interface IFaultInjectionTransactionCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 设定新交易中每个 grain 的值。
        /// </summary>
        /// <param name="grains">下 grains 更新。</param>
        /// <param name="numberToAdd">设定于每个值的数值 grain.</param>
        /// <returns>代表操作的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSet(List<IFaultInjectionTransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新交易中为每个 grain 添加值,并配置错误注入。
        /// </summary>
        /// <param name="grains">下 grains 更新。</param>
        /// <param name="numberToAdd">每项的附加值 grain.</param>
        /// <param name="faultInjection">注射的过错,或 <see langword="null"/> 来进行操作,无受控过失。</param>
        /// <returns>代表操作的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAddAndFaultInjection(List<IFaultInjectionTransactionTestGrain> grains, int numberToAdd,
            FaultInjectionControl? faultInjection = null);
    }

    /// <summary>
    /// 坐标为错误注射试验 grains的往来业务。
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

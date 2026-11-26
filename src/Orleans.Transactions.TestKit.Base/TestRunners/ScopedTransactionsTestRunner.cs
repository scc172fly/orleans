using System;
using System.Threading.Tasks;

using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行通过 <see cref="ITransactionClient"/>创建的交易情景。
    /// </summary>
    public abstract class ScopedTransactionsTestRunner : TransactionTestRunnerBase
    {
        private readonly ITransactionClient _transactionClient;

        /// <summary>
        /// 初始化 <see cref="ScopedTransactionsTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="transactionClient">用于创建交易范围客户端。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ScopedTransactionsTestRunner(IGrainFactory grainFactory, ITransactionClient transactionClient, Action<string> output)
            : base(grainFactory, output)
        {
            _transactionClient = transactionClient;
        }

        /// <summary>
        /// 证明 grain 值可在新创建的交易范围内设定。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValue(string grainStates)
        {
            // 排列排列
            var grain = RandomTestGrain(grainStates);

            // 法案法
            Func<Task> act = () => grain.Set(57);

            await _transactionClient.RunTransaction(TransactionOption.Create, async () =>
                // 发货
                await act.Should().NotThrowAsync(because: "No failure expected"));
        }

        /// <summary>
        /// 证明在新创建的交易范围内发生故障会中止交易。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValueWithFailure(string grainStates)
        {
            // 排列排列
            var grain = RandomTestGrain(grainStates);

            // 法案法
            Func<Task> act = () => _transactionClient.RunTransaction(TransactionOption.Create, () => grain.SetAndThrow(57));

            // 发货
            await act.Should().ThrowAsync<OrleansTransactionAbortedException>(because: "Failure expected");
        }

        /// <summary>
        /// 在一个交易范围内设置并读取 grain 值,并核实交易结果。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValueAndAssert(string grainStates)
        {
            var result = Array.Empty<int>();

            // 排列排列
            var grain = RandomTestGrain(grainStates);

            // 法案法
            await _transactionClient.RunTransaction(TransactionOption.Create, async () =>
            {
                await grain.Set(57);
                result = await grain.Get();
            });

            // 发货
            result.Should().OnlyContain(number => number == 57);
        }

        /// <summary>
        /// 证明中止的嵌套交易并不妨碍外部交易承诺其价值。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task CreateNestedTransactionScopeAndSetValueAndInnerFailAndAssert(string grainStates)
        {
            var result = Array.Empty<int>();

            // 排列排列
            var grain = RandomTestGrain(grainStates);

            // 法案法
            await _transactionClient.RunTransaction(TransactionOption.Create, async () =>
            {
                try
                {
                    await _transactionClient.RunTransaction(TransactionOption.Create, async () => await grain.SetAndThrow(67));
                }
                catch
                { }

                await grain.Set(57);
            });

            result = await grain.Get();

            // 发货
            result.Should().OnlyContain(number => number == 57);
        }
    }
}

using System;
using System.Threading.Tasks;

using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行通过 <see cref="ITransactionClient"/> 创建的事务场景。
    /// </summary>
    public abstract class ScopedTransactionsTestRunner : TransactionTestRunnerBase
    {
        private readonly ITransactionClient _transactionClient;

        /// <summary>
        /// 初始化 <see cref="ScopedTransactionsTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="transactionClient">用于创建事务作用域的客户端。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ScopedTransactionsTestRunner(IGrainFactory grainFactory, ITransactionClient transactionClient, Action<string> output)
            : base(grainFactory, output)
        {
            _transactionClient = transactionClient;
        }

        /// <summary>
        /// 验证能够在新建事务作用域内设置 Grain 的值。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValue(string grainStates)
        {
            // 准备
            var grain = RandomTestGrain(grainStates);

            // 执行
            Func<Task> act = () => grain.Set(57);

            await _transactionClient.RunTransaction(TransactionOption.Create, async () =>
                // 验证
                await act.Should().NotThrowAsync(because: "No failure expected"));
        }

        /// <summary>
        /// 验证新建事务作用域内的失败会中止事务。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValueWithFailure(string grainStates)
        {
            // 准备
            var grain = RandomTestGrain(grainStates);

            // 执行
            Func<Task> act = () => _transactionClient.RunTransaction(TransactionOption.Create, () => grain.SetAndThrow(57));

            // 验证
            await act.Should().ThrowAsync<OrleansTransactionAbortedException>(because: "Failure expected");
        }

        /// <summary>
        /// 在同一个事务作用域中设置并读取 Grain 的值，并验证事务结果。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task CreateTransactionScopeAndSetValueAndAssert(string grainStates)
        {
            var result = Array.Empty<int>();

            // 准备
            var grain = RandomTestGrain(grainStates);

            // 执行
            await _transactionClient.RunTransaction(TransactionOption.Create, async () =>
            {
                await grain.Set(57);
                result = await grain.Get();
            });

            // 验证
            result.Should().OnlyContain(number => number == 57);
        }

        /// <summary>
        /// 验证嵌套事务中止不会阻止外层事务提交其值。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task CreateNestedTransactionScopeAndSetValueAndInnerFailAndAssert(string grainStates)
        {
            var result = Array.Empty<int>();

            // 准备
            var grain = RandomTestGrain(grainStates);

            // 执行
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

            // 验证
            result.Should().OnlyContain(number => number == 57);
        }
    }
}

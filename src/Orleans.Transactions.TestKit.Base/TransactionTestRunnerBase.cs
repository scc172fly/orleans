using System;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 为事务测试运行器提供 Grain 引用创建和输出服务。
    /// </summary>
    public class TransactionTestRunnerBase
    {
        /// <summary>
        /// 用于为测试操作创建引用的 Grain 工厂。
        /// </summary>
        protected readonly IGrainFactory grainFactory;

        /// <summary>
        /// 用于写入测试诊断信息的回调。
        /// </summary>
        protected readonly Action<string> testOutput;

        /// <summary>
        /// 初始化 <see cref="TransactionTestRunnerBase"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于创建测试 Grain 引用的 Grain 工厂。</param>
        /// <param name="testOutput">用于写入测试诊断信息的回调。</param>
        protected TransactionTestRunnerBase(IGrainFactory grainFactory, Action<string> testOutput)
        {
            this.grainFactory = grainFactory;
            this.testOutput = testOutput;
        }

        /// <summary>
        /// 使用新的随机键创建事务测试 Grain 引用。
        /// </summary>
        /// <param name="transactionTestGrainClassNames">事务测试 Grain 的实现类名。</param>
        /// <returns>事务测试 Grain 引用。</returns>
        protected ITransactionTestGrain RandomTestGrain(string transactionTestGrainClassNames)
        {
            return RandomTestGrain<ITransactionTestGrain>(transactionTestGrainClassNames);
        }

        /// <summary>
        /// 使用新的随机键创建测试 Grain 引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 接口类型。</typeparam>
        /// <param name="transactionTestGrainClassNames">事务测试 Grain 的实现类名。</param>
        /// <returns>测试 Grain 引用。</returns>
        protected TGrainInterface RandomTestGrain<TGrainInterface>(string transactionTestGrainClassNames)
            where TGrainInterface : IGrainWithGuidKey
        {
            return TestGrain<TGrainInterface>(transactionTestGrainClassNames, Guid.NewGuid());
        }

        /// <summary>
        /// 使用指定键创建事务测试 Grain 引用。
        /// </summary>
        /// <param name="transactionTestGrainClassName">事务测试 Grain 的实现类名。</param>
        /// <param name="id">Grain 键。</param>
        /// <returns>事务测试 Grain 引用。</returns>
        protected virtual ITransactionTestGrain TestGrain(string transactionTestGrainClassName, Guid id)
        {
            return TestGrain<ITransactionTestGrain>(transactionTestGrainClassName, id);
        }

        /// <summary>
        /// 使用指定键创建测试 Grain 引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">Grain 接口类型。</typeparam>
        /// <param name="transactionTestGrainClassName">事务测试 Grain 的实现类名。</param>
        /// <param name="id">Grain 键。</param>
        /// <returns>测试 Grain 引用。</returns>
        protected virtual TGrainInterface TestGrain<TGrainInterface>(string transactionTestGrainClassName, Guid id)
            where TGrainInterface : IGrainWithGuidKey
        {
            return grainFactory.GetGrain<TGrainInterface>(id, $"{typeof(TGrainInterface).Namespace}.{transactionTestGrainClassName}");
        }
    }
}

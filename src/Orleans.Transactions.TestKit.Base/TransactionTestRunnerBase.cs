using System;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 为交易测试选手提供 grain 参考创建和产出服务。
    /// </summary>
    public class TransactionTestRunnerBase
    {
        /// <summary>
        /// grain 工厂用来为测试作业创建参考。
        /// </summary>
        protected readonly IGrainFactory grainFactory;

        /// <summary>
        /// 用来写测试诊断的回调
        /// </summary>
        protected readonly Action<string> testOutput;

        /// <summary>
        /// 初始化 <see cref="TransactionTestRunnerBase"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于创建测试的工厂 grain 参考文献。</param>
        /// <param name="testOutput">用于写作测试诊断的回调。</param>
        protected TransactionTestRunnerBase(IGrainFactory grainFactory, Action<string> testOutput)
        {
            this.grainFactory = grainFactory;
            this.testOutput = testOutput;
        }

        /// <summary>
        /// 创建交易测试 grain 引用,并使用新的随机密钥。
        /// </summary>
        /// <param name="transactionTestGrainClassNames">交易测试 grain 执行类名称。</param>
        /// <returns>交易测试 grain 参考。</returns>
        protected ITransactionTestGrain RandomTestGrain(string transactionTestGrainClassNames)
        {
            return RandomTestGrain<ITransactionTestGrain>(transactionTestGrainClassNames);
        }

        /// <summary>
        /// 使用新的随机密钥创建测试 grain 引用。
        /// </summary>
        /// <typeparam name="TGrainInterface"> grain 接口类型。</typeparam>
        /// <param name="transactionTestGrainClassNames">交易测试 grain 执行类名称。</param>
        /// <returns>测试试验 grain 参考。</returns>
        protected TGrainInterface RandomTestGrain<TGrainInterface>(string transactionTestGrainClassNames)
            where TGrainInterface : IGrainWithGuidKey
        {
            return TestGrain<TGrainInterface>(transactionTestGrainClassNames, Guid.NewGuid());
        }

        /// <summary>
        /// 创建指定密钥的交易测试 grain 参考。
        /// </summary>
        /// <param name="transactionTestGrainClassName">交易测试 grain 执行类名称。</param>
        /// <param name="id"> grain 密钥。</param>
        /// <returns>交易测试 grain 参考。</returns>
        protected virtual ITransactionTestGrain TestGrain(string transactionTestGrainClassName, Guid id)
        {
            return TestGrain<ITransactionTestGrain>(transactionTestGrainClassName, id);
        }

        /// <summary>
        /// 创建指定密钥的测试 grain 引用。
        /// </summary>
        /// <typeparam name="TGrainInterface"> grain 接口类型。</typeparam>
        /// <param name="transactionTestGrainClassName">交易测试 grain 执行类名称。</param>
        /// <param name="id"> grain 密钥。</param>
        /// <returns>测试试验 grain 参考。</returns>
        protected virtual TGrainInterface TestGrain<TGrainInterface>(string transactionTestGrainClassName, Guid id)
            where TGrainInterface : IGrainWithGuidKey
        {
            return grainFactory.GetGrain<TGrainInterface>(id, $"{typeof(TGrainInterface).Namespace}.{transactionTestGrainClassName}");
        }
    }
}

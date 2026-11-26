using System.Threading.Tasks;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="TocGoldenPathTestRunner"/>
    public abstract class TocGoldenPathTestRunnerxUnit : TocGoldenPathTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="TocGoldenPathTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output"> xUnit 测试输出助手。</param>
        protected TocGoldenPathTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
        : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output))) { }

        /// <inheritdoc cref="TocGoldenPathTestRunner.MultiGrainWriteTransaction(string, int)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions / 2)]
        public override Task MultiGrainWriteTransaction(string grainStates, int grainCount)
        {
            return base.MultiGrainWriteTransaction(grainStates, grainCount);
        }
    }
}

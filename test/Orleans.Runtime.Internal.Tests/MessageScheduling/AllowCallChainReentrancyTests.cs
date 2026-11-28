using TestExtensions;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 测试非可重入 Grain 上允许调用链重入的场景。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class AllowCallChainReentrancyTests : OrleansTestingBase, IClassFixture<AllowCallChainReentrancyTests.Fixture>
    {
        private const int NumIterations = 30;
        private readonly CallChainReentrancyTestHelper _testHelper;

        public class Fixture : BaseTestClusterFixture
        {
        }

        public AllowCallChainReentrancyTests(ITestOutputHelper output, Fixture fixture)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            _testHelper = new CallChainReentrancyTestHelper
            {
                Fixture = fixture,
                NumIterations = NumIterations
            };
        }

        // 1）允许重入 A, A。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task DeadlockDetection_1()
        {
            await _testHelper.CallChainReentrancy_1();
        }

        // 2）允许非可重入 Grain 上的重入 A, B, A。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_2()
        {
            await _testHelper.CallChainReentrancy_2();
        }

        // 3）允许重入 X, A, X, A。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_3()
        {
            await _testHelper.CallChainReentrancy_3();
        }

        // 4）无死锁 X, X。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_4()
        {
            await _testHelper.CallChainReentrancy_4();
        }

        // 5）无死锁 X, A, X。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_5()
        {
            await _testHelper.CallChainReentrancy_5();
        }

        // 6）允许非可重入 Grain 上的重入 A, B, C, A。
        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_6()
        {
            await _testHelper.CallChainReentrancy_6();
        }

        [Fact, TestCategory("Functional"), TestCategory("Deadlock")]
        public async Task CallChainReentrancy_WithSuppression()
        {
            await _testHelper.CallChainReentrancy_WithSuppression();
        }
    }
}

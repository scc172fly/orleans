using System;
using System.Threading.Tasks;
using AwesomeAssertions.Equivalency;
using Orleans.Transactions.Abstractions;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}"/>
    public abstract class TransactionalStateStorageTestRunnerxUnit<TState> : TransactionalStateStorageTestRunner<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="stateStorageFactory">要创建工厂 ITransactionalStateStorage,测试选手将假设状态 
        /// 创建 ITransactionalStateStorage </param>时存储为空
        /// <param name="stateFactory">创建用于测试的 T State 工厂</param>
        /// <param name="grainFactory">grain 测试跑者所需的工厂</param>
        /// <param name="testOutput">测试输出到帮助信息</param>
        /// <param name="assertConfig">提及 AwesomeAssertions.Equivalency.EquivalencyOptions`1
        ///     可用于影响对象图形方式的配置对象
        ///     比较</param>
        public TransactionalStateStorageTestRunnerxUnit(Func<Task<ITransactionalStateStorage<TState>>> stateStorageFactory,
            Func<int, TState> stateFactory, IGrainFactory grainFactory, ITestOutputHelper testOutput,
            Func<EquivalencyOptions<TState>, EquivalencyOptions<TState>>? assertConfig = null)
            : base(stateStorageFactory, stateFactory, grainFactory, TestOutputHelperExtensions.GetWriteLine(testOutput, nameof(testOutput)), assertConfig)
        {
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.FirstTime_Load_ShouldReturnEmptyLoadResponse"/>
        [Fact]
        public override Task FirstTime_Load_ShouldReturnEmptyLoadResponse()
        {
            return base.FirstTime_Load_ShouldReturnEmptyLoadResponse();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.StoreWithoutChanges"/>
        [Fact]
        public override Task StoreWithoutChanges()
        {
            return base.StoreWithoutChanges();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.WrongEtags"/>
        [Fact]
        public override Task WrongEtags()
        {
            return base.WrongEtags();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ConfirmOne(bool)"/>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public override Task ConfirmOne(bool useTwoSteps)
        {
            return base.ConfirmOne(useTwoSteps);
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.CancelOne"/>
        [Fact]
        public override Task CancelOne()
        {
            return base.CancelOne();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ReplaceOne"/>
        [Fact]
        public override Task ReplaceOne()
        {
            return base.ReplaceOne();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ConfirmOneAndCancelOne(bool, bool)"/>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public override Task ConfirmOneAndCancelOne(bool useTwoSteps, bool reverseOrder)
        {
            return base.ConfirmOneAndCancelOne(useTwoSteps, reverseOrder);
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.GrowingBatch"/>
        [Fact]
        public override Task GrowingBatch()
        {
            return base.GrowingBatch();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ShrinkingBatch"/>
        [Fact]
        public override Task ShrinkingBatch()
        {
            return base.ShrinkingBatch();
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.PrepareMany(int)"/>
        [Theory]
        [InlineData(99)]
        [InlineData(100)]
        [InlineData(200)]
        public override Task PrepareMany(int count)
        {
            return base.PrepareMany(count);
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ConfirmMany(int, bool)"/>
        [Theory]
        [InlineData(99, true)]
        [InlineData(99, false)]
        [InlineData(100, true)]
        [InlineData(100, false)]
        [InlineData(200, true)]
        [InlineData(200, false)]
        public override Task ConfirmMany(int count, bool useTwoSteps)
        {
            return base.ConfirmMany(count, useTwoSteps);
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.CancelMany(int)"/>
        [Theory]
        [InlineData(99)]
        [InlineData(100)]
        [InlineData(200)]
        public override Task CancelMany(int count)
        {
            return base.CancelMany(count);
        }

        /// <inheritdoc cref="TransactionalStateStorageTestRunner{TState}.ReplaceMany(int)"/>
        [Theory]
        [InlineData(99)]
        [InlineData(100)]
        [InlineData(200)]
        public override Task ReplaceMany(int count)
        {
            return base.ReplaceMany(count);
        }
    }
}

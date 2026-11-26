using Orleans.Tests.SqlUtils;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// MySQL的持久性测试。
    /// </summary>
    /// <remarks>
    /// 将这些测试复制到任何后端, 不仅仅是相关、 复制和粘贴
    /// 类别,可选择删除 <see cref="RelationalStorageTests"/> 继承并落实
    /// 与 <see cref="CommonFixture"/> 中的做法一样,以及该规定如何代表它,并设置了提供方和环境装置。
    /// </remarks>
    [TestCategory("MySql"), TestCategory("Persistence")]
    [TestSuite("Functional")]
    [TestProvider("MySql")]
    [TestArea("Persistence")]
    public class MySqlStorageTests : RelationalStorageTests, IClassFixture<CommonFixture>
    {
        /// <summary>
        /// 本测试集的存储变量、 存储代号或 ADO.NET 变量。
        /// </summary>
        private const string AdoNetInvariant = AdoNetInvariants.InvariantNameMySql;

        public MySqlStorageTests(CommonFixture commonFixture) : base(AdoNetInvariant, commonFixture)
        {
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task VersionWrapsAfterMaximum()
        {
            await Relational_VersionWrapsAfterMaximum();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()
        {
            await Relational_WriteDuplicateFailsWithInconsistentStateException();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task StorageDataSetGeneric_HashCollisionTests()
        {
            await Relational_HashCollisionTests();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task GrainTypeQualifiedKeyFormat_IsolatesGrains()
        {
            await Relational_GrainTypeQualifiedKeyFormatIsolatesGrains();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task LegacyKeyFormat_SharesRowAcrossGrainTypes()
        {
            await Relational_LegacyKeyFormatSharesRowAcrossGrainTypes();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task WriteInconsistentFailsWithIncosistentStateException()
        {
            await Relational_WriteInconsistentFailsWithIncosistentStateException();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task WriteRead100StatesInParallel()
        {
            await Relational_WriteReadWriteRead100StatesInParallel();
        }

        [Fact]
        [TestCategory("Functional"), TestCategory("ModelBased")]
        public async Task GrainStorage_ModelBasedGeneratedConformance()
        {
            await Relational_ModelBasedGeneratedConformance("MySql");
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task WriteReadCyrillic()
        {
            await PersistenceStorageTests.PersistenceStorage_Relational_WriteReadIdCyrillic();
        }

        [Theory, ClassData(typeof(StorageDataSet2CyrillicIdsAndGrainNames<string>))]
        [TestCategory("Functional")]
        internal async Task DataSet2_Cyrillic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSet2CyrillicIdsAndGrainNames<string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetPlain<long>))]
        [TestCategory("Functional")]
        internal async Task PersistenceStorage_StorageDataSetPlain_IntegerKey_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetPlain<long>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<Guid, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_GuidKey_Generic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<Guid, string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<long, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_IntegerKey_Generic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<long, string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<string, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_Json_WriteRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<string, string>.GetTestData(testNum);
            await this.Relational_Json_WriteRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<string, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_StringKey_Generic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<string, string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<string, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_Binary_WriteRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<string, string>.GetTestData(testNum);
            await this.Relational_Binary_WriteRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetPlain<Guid>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetPlain_GuidKey_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetPlain<Guid>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetPlain<string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetPlain_StringKey_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetPlain<string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }
    }
}

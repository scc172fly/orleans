using Orleans.Tests.SqlUtils;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// PostgreSQL 持久化测试。
    /// </summary>
    /// <remarks>若要在任意后端（不限于关系型后端）复用这些测试，请复制粘贴此类，
    /// 可选择移除对 <see cref="RelationalStorageTests"/> 的继承，并实现提供程序和环境的
    /// 初始化，参照 <see cref="CommonFixture"/> 及其委托方式。</remarks>
    [TestCategory("PostgreSql"), TestCategory("Persistence")]
    [TestSuite("Functional")]
    [TestProvider("PostgreSql")]
    [TestArea("Persistence")]
    public class PostgreSqlStorageTests : RelationalStorageTests, IClassFixture<CommonFixture>
    {
        /// <summary>
        /// 此测试集的存储固定名称、存储 ID 或 ADO.NET 固定名称。
        /// </summary>
        private const string AdoNetInvariant = AdoNetInvariants.InvariantNamePostgreSql;

        public PostgreSqlStorageTests(CommonFixture commonFixture) : base(AdoNetInvariant, commonFixture)
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
        public async Task WriteReadCyrillic()
        {
            await PersistenceStorageTests.PersistenceStorage_Relational_WriteReadIdCyrillic();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task WriteReadWriteRead100StatesInParallel()
        {
            await Relational_WriteReadWriteRead100StatesInParallel();
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
        public async Task WriteDuplicateFailsWithInconsistentStateException()
        {
            await Relational_WriteDuplicateFailsWithInconsistentStateException();
        }

        [Fact]
        [TestCategory("Functional")]
        public async Task WriteInconsistentFailsWithIncosistentStateException()
        {
            await Relational_WriteInconsistentFailsWithIncosistentStateException();
        }

        [Fact]
        [TestCategory("Functional"), TestCategory("ModelBased")]
        public async Task GrainStorage_ModelBasedGeneratedConformance()
        {
            await Relational_ModelBasedGeneratedConformance("PostgreSql");
        }

        [Theory, ClassData(typeof(StorageDataSetPlain<long>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetPlain_IntegerKey_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetPlain<long>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
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
        internal async Task PersistenceStorage_StorageDataSetPlain_StringKey_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetPlain<string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSet2CyrillicIdsAndGrainNames<string>))]
        [TestCategory("Functional")]
        internal async Task DataSet2_Cyrillic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSet2CyrillicIdsAndGrainNames<string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<long, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_IntegerKey_Generic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<long, string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<Guid, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_GuidKey_Generic_WriteClearRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<Guid, string>.GetTestData(testNum);
            await this.PersistenceStorageTests.Store_WriteClearRead(grainType, getGrain, grainState);
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
        internal async Task StorageDataSetGeneric_Json_WriteRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<string, string>.GetTestData(testNum);
            await this.Relational_Json_WriteRead(grainType, getGrain, grainState);
        }

        [Theory, ClassData(typeof(StorageDataSetGeneric<string, string>))]
        [TestCategory("Functional")]
        internal async Task StorageDataSetGeneric_Binary_WriteRead(int testNum)
        {
            var (grainType, getGrain, grainState) = StorageDataSetGeneric<string, string>.GetTestData(testNum);
            await this.Relational_Binary_WriteRead(grainType, getGrain, grainState);
        }
    }
}

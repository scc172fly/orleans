using Orleans.Storage;
using System.Globalization;
using System.Text;
using Xunit;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 各种辅助人员课程的考试。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Persistence")]
    public class AdotNetProviderFunctionalityTests
    {
        [TestCategory("Functional"), TestCategory("Persistence")]
        [Fact]
        public void AdoNetStorageProviderGrainTypeHashing()
        {
            //使用这种方法 使用草草地就像 ADO.NET 储存供应商会使用它。
            //该哈希器是线程安全的。
            var adonetDefaultHasher = new StorageHasherPicker(new[] { new OrleansDefaultHasher() });
            const int TestGrainHash = -201809205;
            var grainType = "Grains.PersonGrain";
            Parallel.For(0, 1000000, i =>
            {
                //本次测试中这些参数可以是空的 。
                var hasher = adonetDefaultHasher.PickHasher<object>(null!, null!, null!, default, null!, null!);
                Assert.NotNull(hasher);
                int grainTypeHash = hasher.Hash(Encoding.UTF8.GetBytes(grainType));
                Assert.Equal(TestGrainHash, grainTypeHash);
            });
        }

        [TestCategory("Functional"), TestCategory("Persistence")]
        [Fact]
        public void LongGrainIdToN1KeyAreSame()
        {
            const long LongGrainId = 1001;
            var longGrainIdAsN1 = new AdoGrainKey(LongGrainId, null);

            Assert.Equal(LongGrainId, longGrainIdAsN1.N1Key);
        }

        [TestCategory("Functional"), TestCategory("Persistence")]
        [Fact]
        public void LongGrainIdToStringAreSame()
        {
            const long LongGrainId = 1001;
            var longGrainIdAsString = new AdoGrainKey(LongGrainId, null).ToString();

            Assert.Equal(longGrainIdAsString, LongGrainId.ToString(CultureInfo.InvariantCulture));
        }

        [TestCategory("Functional"), TestCategory("Persistence")]
        [Fact]
        public void LongGrainIdWithExtensionAreSame()
        {
            const long LongGrainId = 1001;
            const string ExtensionKey = "ExtensionKey";
            var longGrainIdWitExtensionAsString = new AdoGrainKey(LongGrainId, ExtensionKey).ToString();

            //AdoGrainKey 辅助类使用字符“#”分割 grain 键和扩展键。
            //关键及其延伸是两个不同的要素。
            var grainKeys = longGrainIdWitExtensionAsString.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, grainKeys.Length);

            Assert.Equal(grainKeys[0], LongGrainId.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(ExtensionKey, grainKeys[1]);
        }

        [TestCategory("Functional"), TestCategory("Persistence")]
        [Fact]
        public void GuidGrainIdWithExtensionAreSame()
        {
            Guid guidId = Guid.Parse("751D8030-9C84-4A91-816E-E95F64CE7588");
            var guidIdAsString = new AdoGrainKey(guidId, null).ToString();

            Assert.Equal(guidIdAsString, guidId.ToString());
        }
    }
}

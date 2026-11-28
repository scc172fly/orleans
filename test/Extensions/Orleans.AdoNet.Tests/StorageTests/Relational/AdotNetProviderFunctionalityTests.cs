using Orleans.Storage;
using System.Globalization;
using System.Text;
using Xunit;


namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 各种辅助类的测试。
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
            //这种使用哈希器的方式与 ADO.NET 存储提供程序的使用方式相同，旨在测试
            //哈希器是否具有线程安全性。
            var adonetDefaultHasher = new StorageHasherPicker(new[] { new OrleansDefaultHasher() });
            const int TestGrainHash = -201809205;
            var grainType = "Grains.PersonGrain";
            Parallel.For(0, 1000000, i =>
            {
                //在此测试中，这些参数可以为 null。
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

            //AdoGrainKey 辅助类使用字符 '#' 分隔 Grain 键和扩展键。
            //键及其扩展是两个不同的组成部分。
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

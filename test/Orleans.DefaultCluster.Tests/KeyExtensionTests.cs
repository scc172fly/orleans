using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans Grain 键扩展功能的测试。
    /// 键扩展允许将主键
    /// （GUID、long 或字符串）与字符串扩展部分组合成复合键。这支持
    /// 多租户等场景，同一 Grain 类型可为不同租户拥有不同实例。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class KeyExtensionTests : HostedTestClusterEnsureDefaultStarted
    {
        public KeyExtensionTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 验证具有相同基础主键但不同键扩展的 Grain
        /// 被视为具有独立激活实例的不同 Grain 实例。
        /// 这对于多租户和分区场景至关重要。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public async Task PrimaryKeyExtensionsShouldDifferentiateGrainsUsingTheSameBasePrimaryKey()
        {
            var baseKey = Guid.NewGuid();

            const string kx1 = "1";
            const string kx2 = "2";

            var grain1 = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, kx1, null);
            var grainId1 = await grain1.GetGrainReference();
            var activationId1 = await grain1.GetActivationId();

            var grain2 = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, kx2, null);
            var grainId2 = await grain2.GetGrainReference();
            var activationId2 = await grain2.GetActivationId();

            Assert.NotEqual(grainId1, grainId2); // 不同的键扩展应能区分具有相同基础主键的 Grain。
            Assert.NotEqual(activationId1, activationId2); // 不同的键扩展应能区分具有相同基础主键的 Grain。
        }

        /// <summary>
        /// 验证具有不同基础主键的 Grain 是不同的 Grain，
        /// 即使它们共享相同的键扩展值。
        /// 确保不同 Grain 实例之间的正确隔离。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public async Task PrimaryKeyExtensionsShouldDifferentiateGrainsUsingDifferentBaseKeys()
        {
            var baseKey1 = Guid.NewGuid();
            var baseKey2 = Guid.NewGuid();

            const string kx = "1";

            var grain1 = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey1, kx, null);
            var grainId1 = await grain1.GetGrainReference();
            var activationId1 = await grain1.GetActivationId();

            var grain2 = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey2, kx, null);
            var grainId2 = await grain2.GetGrainReference();
            var activationId2 = await grain2.GetActivationId();

            Assert.NotEqual(grainId1, grainId2); // 不同的基础键应能区分具有相同扩展部分的键。
            Assert.NotEqual(activationId1, activationId2); // 不同的基础键应能区分具有相同扩展部分的键。
        }

        /// <summary>
        /// 验证不允许使用空字符串键扩展。
        /// 这可防止 Grain 标识意外混淆，并确保
        /// 使用键扩展时扩展部分具有实际含义。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public void EmptyKeyExtensionsAreDisallowed()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var baseKey = Guid.NewGuid();

                this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, "", null);
            });
        }

        /// <summary>
        /// 验证不允许使用仅包含空白字符的键扩展。
        /// 这确保键扩展包含有意义的标识符，
        /// 并防止由不可见字符引发的隐蔽错误。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public void WhiteSpaceKeyExtensionsAreDisallowed()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var baseKey = Guid.NewGuid();

                this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, " \t\n\r", null);
            });
        }

        /// <summary>
        /// 验证不允许使用 null 键扩展。
        /// 强制要求在使用支持扩展键的 Grain 工厂方法时
        /// 显式提供键扩展。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public void NullKeyExtensionsAreDisallowed()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                var baseKey = Guid.NewGuid();

                this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, null!, null);
            });
        }

        /// <summary>
        /// 验证键扩展长度可以超过 127 字节。
        /// 这确保系统能处理较长的标识符，例如
        /// 文件路径、URL 或其他扩展标识符，不受人为长度限制。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public async Task PrimaryKeyExtensionsShouldPermitStringsLongerThan127BytesLong()
        {
            var baseKey = Guid.NewGuid();

            string kx1 = new string('\\', 300);

            var localGrainRef = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, kx1, null);
            var remoteGrainRef = await localGrainRef.GetGrainReference();

            Assert.Equal(localGrainRef, remoteGrainRef); // Grain ID 不匹配。
        }

        /// <summary>
        /// 测试从 Grain 引用获取主键字符串。
        /// 验证可从使用字符串键的 Grain 的引用中提取其键，
        /// 用于诊断或路由。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public void GetPrimaryKeyStringOnGrainReference()
        {
            const string key = "foo";

            var grain = this.GrainFactory.GetGrain<IStringGrain>(key);
            var key2 = ((GrainReference)grain).GetPrimaryKeyString();

            Assert.Equal(key, key2); // 返回了意外的键。
        }
    }
}

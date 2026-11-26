using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans grain 键扩展功能测试。
    /// 密钥扩展允许 grains 通过合并主键组合组合组合组合键复用密钥
    /// ( GUID、 长或字符串) 带有字符串扩展名。 这允许使用类似
    /// 同一 grain 类型下每个租户的情况不同,多租房。
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
        /// 证明 grains 具有相同的基主键但不同的键扩展
        /// 将分别作为 grain 不同实例处理,并单独启动。
        /// 这是多租期和分治情景的基本假设。
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

            Assert.NotEqual(grainId1, grainId2); // 配错的密钥扩展名应该区分一个相同的基底主键。
            Assert.NotEqual(activationId1, activationId2); // 配错的密钥扩展名应该区分一个相同的基底主键。
        }

        /// <summary>
        /// 证明 grains 具有不同基底主键是独立的
        /// 即使它们具有相同的关键扩展值。
        /// 确保在不同 grain 实例之间保持适当的隔离。
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

            Assert.NotEqual(grainId1, grainId2); // 基准键应区分相同的扩展键, 其大小不匹配。
            Assert.NotEqual(activationId1, activationId2); // 基准键应区分相同的扩展键, 其大小不匹配。
        }

        /// <summary>
        /// 验证不允许使用空字符串密钥扩展名 。
        /// 防止意外 grain 身份混淆,并确保
        /// 使用时的有意义的键扩展名。
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
        /// 证明只允许空白密钥扩展 。
        /// 这确保关键扩展号包含有意义的识别特征
        /// 防止隐微的虫子从隐形字符中消失。
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
        /// 证明不允许无名密钥扩展 。
        /// 强制规定关键延长期必须明确规定
        /// 当使用扩展 grain 工厂方法时。
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
        /// 证明密钥扩展可超过127字节长度。
        /// 这确保该系统能够处理长识别特征,例如
        /// 文件路径、 URL 或其他无人为限制的扩展标识符 。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public async Task PrimaryKeyExtensionsShouldPermitStringsLongerThan127BytesLong()
        {
            var baseKey = Guid.NewGuid();

            string kx1 = new string('\\', 300);

            var localGrainRef = this.GrainFactory.GetGrain<IKeyExtensionTestGrain>(baseKey, kx1, null);
            var remoteGrainRef = await localGrainRef.GetGrainReference();

            Assert.Equal(localGrainRef, remoteGrainRef); // ID 错误的 grain 。
        }

        /// <summary>
        /// 从 grain 引用中获取主键字符串的测试。
        /// 验证密钥对 grains 可以提取他们的密钥
        /// 从 grain 参考文献中取出,用于诊断或路由目的。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("PrimaryKeyExtension")]
        public void GetPrimaryKeyStringOnGrainReference()
        {
            const string key = "foo";

            var grain = this.GrainFactory.GetGrain<IStringGrain>(key);
            var key2 = ((GrainReference)grain).GetPrimaryKeyString();

            Assert.Equal(key, key2); // 意外的密钥被返回 。
        }
    }
}

using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests
{
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Orleans' grain 参考铸造能力的测试。
    /// 验证 grain 引用可在兼容的接口之间安全投放,
    /// 包括向基界面更新,在多个已安装的接口之间进行选择,
    /// 以及无效投影的适当错误处理。测试激活前和启动后两种情景。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainReferenceCastTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly IInternalGrainFactory internalGrainFactory;

        public GrainReferenceCastTests(DefaultClusterFixture fixture) : base(fixture)
        {
            var client = this.HostedCluster.Client!;
            this.internalGrainFactory = client.ServiceProvider.GetRequiredService<IInternalGrainFactory>();
        }

        /// <summary>
        /// 测试基本 grain 引用投影到相同的界面类型。
        /// 校验正确打印 grain 引用其自接口类型工作
        /// 返回可指定给预期类型的引用。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefCastFromMyType()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);
            GrainReference cast = (GrainReference)grain.AsReference<ISimpleGrain>();
            Assert.IsAssignableFrom(grain.GetType(), cast);
            Assert.IsAssignableFrom<ISimpleGrain>(cast);
        }

        /// <summary>
        /// 由同一 grain执行的多个接口之间进行切换试验。
        /// 验证一个 grain 执行的多界面可在
        /// 这些界面在保持适当类型关系的同时,保持适当的类型关系。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefCastFromMyTypePolymorphic()
        {
            IAddressable grain = this.GrainFactory.GetGrain<IMultifacetTestGrain>(0);
            Assert.IsAssignableFrom<IMultifacetWriter>(grain);
            Assert.IsAssignableFrom<IMultifacetReader>(grain);

            IAddressable cast = grain.AsReference<IMultifacetReader>();
            Assert.IsAssignableFrom(grain.GetType(), cast);
            Assert.IsAssignableFrom<IMultifacetWriter>(cast);
            Assert.IsAssignableFrom<IMultifacetReader>(grain);

            IAddressable cast2 = grain.AsReference<IMultifacetWriter>();
            Assert.IsAssignableFrom(grain.GetType(), cast2);
            Assert.IsAssignableFrom<IMultifacetReader>(cast2);
            Assert.IsAssignableFrom<IMultifacetWriter>(grain);
        }

        /// <summary>
        /// 测试在多面 grain上读者与作家界面之间的切换。
        /// 验证能够访问实施多个相关接口的 grains
        /// 通过不同互动观点,同时保持国家一致性。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task CastMultifacetRWReference()
        {
            int newValue = 3;

            IMultifacetWriter writer = this.GrainFactory.GetGrain<IMultifacetWriter>(1);
            // 试验箱里不准等待

            IMultifacetReader reader = writer.AsReference<IMultifacetReader>();

            await writer.SetValue(newValue);

            Task<int> readAsync = reader.GetValue();
            int result = await readAsync;

            Assert.Equal(newValue, result);
        }

        /// <summary>
        /// 测试在有清晰 grain 分辨率的界面之间进行切换。
        /// 当 grain 引用解析时验证接口立件行为
        /// 在铸造前,确保适当的接口兼容性检查。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task CastMultifacetRWReferenceWaitForResolve()
        {
            int newValue = 4;

            IMultifacetWriter writer = this.GrainFactory.GetGrain<IMultifacetWriter>(2);

            IMultifacetReader reader = writer.AsReference<IMultifacetReader>();

            await writer.SetValue(newValue);

            int result = await reader.GetValue();

            Assert.Equal(newValue, result);
        }

        /// <summary>
        /// 无效的试镜尝试会给人适当的例外
        /// 验证试图投出 grain 所指的非grain 类型
        /// 产生 ArgumentException。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastFailInternalCastFromBadType()
        {
            var grain = this.GrainFactory.GetGrain<ISimpleGrain>(
                Random.Shared.Next(),
                SimpleGrain.SimpleGrainNamePrefix);

            // 试图将 grain 投入非grain 类型时失败 。
            Assert.Throws<ArgumentException>(() => this.internalGrainFactory.Cast(grain, typeof(bool)));
        }

        /// <summary>
        /// 测试同类铸造的内置铸造机制。
        /// 验证将 grain 投入其界面类型时是否优化
        /// ,返回同一引用对象。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastInternalCastFromMyType()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);

            // 这块石膏应该是无操作的 因为接口与最初引用的完全吻合
            IAddressable cast = grain.Cast<ISimpleGrain>();

            Assert.Same(grain, cast);
            Assert.IsAssignableFrom<ISimpleGrain>(cast);
        }

        /// <summary>
        /// 从源代码向基 grain 接口上调测试。
        /// 校验从导出接口可投出 grain 引用
        /// 遵循正常继承规则,与基本接口接轨。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastInternalCastUpFromChild()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());

            // 该投影应不选,因为界面由初始参考界面执行。
            IAddressable cast = grain.Cast<IGeneratorTestGrain>();

            Assert.Same(grain, cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);
        }

        /// <summary>
        /// 测试 grain 引用通过 AsReference 方法上调预报的方法。
        /// 校验导出 grain 引用可上向基界面
        /// 使用 AsReference API ,同时保持类型兼容性。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefUpCastFromChild()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());
            GrainReference cast = (GrainReference)grain.AsReference<IGeneratorTestGrain>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedGrain1>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);
        }

        /// <summary>
        /// 测试在 grain 分辨率后, sibling界面之间的侧投显示失败 。
        /// 试图在不相关的界面(
        /// 抛(下定数) InvalidCastException 时 grain 已经解决了 。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task FailSideCastAfterResolve()
        {
            IGeneratorTestDerivedGrain1 grain = this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());
            Assert.True(await grain.StringIsNullOrEmpty());

            IGeneratorTestDerivedGrain2 cast = grain.AsReference<IGeneratorTestDerivedGrain2>();

            await Assert.ThrowsAsync<InvalidCastException>(() => cast.StringConcat("a", "b", "c"));
        }

        /// <summary>
        /// 测试操作在无效的侧向播放尝试后失败 。
        /// 校验错误投出 grain 参 [ 引用上的调用方法
        /// 扔弃 InvalidCastException 实际时间 grain 无法执行接口
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task FailOperationAfterSideCast()
        {
            IGeneratorTestDerivedGrain1 grain = this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());

            // 尚未解决 grain 参考值时, 种子工程乐观地进行
            IGeneratorTestDerivedGrain2 cast = grain.AsReference<IGeneratorTestDerivedGrain2>();

            // 完全解决 grain 引用时操作失败
            await Assert.ThrowsAsync<InvalidCastException>(() => cast.StringConcat("a", "b", "c"));
        }

        /// <summary>
        /// 测试复杂的铸造假想,并使用连续链。
        /// 证明无效抛射在同步持续情况下不合格,
        /// 共同接口的有效操作继续工作。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task FailSideCastAfterContinueWith()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());
            IGeneratorTestDerivedGrain2? cast = null;
            var av = grain.StringIsNullOrEmpty();
            var av2 = av.ContinueWith(t => Assert.True(t.Result), cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        Assert.False(t.IsFaulted);

                        // 总是允许进行种化,因此这应该成功。
                        cast = grain.AsReference<IGeneratorTestDerivedGrain2>();
                    },
                    cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        // 调用 grain 没有执行的方法,导致抛射失败。
                        Assert.True(t.IsCompletedSuccessfully);
                        return cast!.StringConcat("a", "b", "c");
                    },
                    cancellationToken)
                .Unwrap()
                .ContinueWith(
                    t =>
                    {
                        // 在通用接口上调用一个方法,由 grain 执行。
                        // 这不应该扔。
                        Assert.True(t.IsFaulted);
                        return cast!.StringIsNullOrEmpty();
                    },
                    cancellationToken)
                .Unwrap();

            // 确保最后一项任务不会被丢弃。
            var av2Result = await av2;
            Assert.True(av2Result);
        }

        /// <summary>
        /// 测试通过多个层次的接口继承进行更新。
        /// 校验可从孙子女界面中投出 grain 参考文献
        /// 父母和祖父母的接口,并进行适当的类型检查
        /// 防止无效的跨等级铸造。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefUpCastFromGrandchild()
        {
            GrainReference cast;
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedDerivedGrain>(GetRandomGrainId());

            // 父母
            cast = (GrainReference)grain.AsReference<IGeneratorTestDerivedGrain2>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedDerivedGrain>(cast);
            Assert.IsAssignableFrom<IGeneratorTestDerivedGrain2>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);

            // 继承等级制度之外交叉广播不应奏效
            Assert.False(cast is IGeneratorTestDerivedGrain1);

            // 祖父母
            cast = (GrainReference)grain.AsReference<IGeneratorTestGrain>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedDerivedGrain>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);

            // 继承等级制度之外交叉广播不应奏效
            Assert.False(cast is IGeneratorTestDerivedGrain1);
        }

        /// <summary>
        /// 测试从深嵌巢衍生界面上进行检测。
        /// 从接口中选择时验证适当类型关系
        /// 继承等级制度深层的多个层次。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefUpCastFromDerivedDerivedChild()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedDerivedGrain>(GetRandomGrainId());
            GrainReference cast = (GrainReference)grain.AsReference<IGeneratorTestDerivedGrain2>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedDerivedGrain>(cast);
            Assert.IsAssignableFrom<IGeneratorTestDerivedGrain2>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);
            Assert.False(cast is IGeneratorTestDerivedGrain1);
        }

        /// <summary>
        /// 自播 grain 引用时测试同步操作。
        /// 验证 grain 方法在铸完后正确调用
        /// a grain 指其自己的接口类型。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task CastAsyncGrainRefCastFromSelf()
        {
            IAddressable grain = this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);
            ISimpleGrain cast = grain.AsReference<ISimpleGrain>();

            Task<int> successfulCallPromise = cast.GetA();
            await successfulCallPromise;
            Assert.Equal(TaskStatus.RanToCompletion, successfulCallPromise.Status);
        }

        // 待办: 执行 白框访问
#if TODO
        [Fact]
        public void CastAsyncGrainRefUpCastFromChild()
        {
            // GeneratorTestDerivedGrain1Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedGrain2Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedDerivedGrainReference 源自 GeneratorTestDerivedGrain2Reference

            //GrainReference grain = GeneratorTestDerivedGrain1Reference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "",
                "GeneratorTestGrain.GeneratorTestDerivedGrain1" );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestGrainFactory.Cast(grain);
            Assert.NotNull(cast);
            //Assert.Same(类型[IGeneratorTestGrain], cast.GetType());

            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解决 grain 参 号
            }

            Assert.True(cast.IsResolved);
            Assert.True(grain.IsResolved);
        }

        [Fact]
        public void CastAsyncGrainRefUpCastFromGrandchild()
        {
            // GeneratorTestDerivedGrain1Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedGrain2Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedDerivedGrainReference 源自 GeneratorTestDerivedGrain2Reference

            //GrainReference grain = GeneratorTestDerivedDerivedGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestDerivedDerivedGrain"
            );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestGrainFactory.Cast(grain);
            Assert.NotNull(cast);
            //Assert.Same(类型[IGeneratorTestGrain], cast.GetType());

            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解决 grain 参 号
            }

            Assert.True(cast.IsResolved);
            Assert.True(grain.IsResolved);
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailSideCastToPeer()
        {
            // GeneratorTestDerivedGrain1Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedGrain2Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedDerivedGrainReference 源自 GeneratorTestDerivedGrain2Reference

            //GrainReference grain = GeneratorTestDerivedGrain1Reference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestDerivedGrain1"
            );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedGrain2Factory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解决 grain 参 号
            }

            Assert.True(false, "Exception should have been raised");
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailDownCastToChild()
        {
            // GeneratorTestDerivedGrain1Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedGrain2Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedDerivedGrainReference 源自 GeneratorTestDerivedGrain2Reference

            //GrainReference grain = GeneratorTestGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestGrain");
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedGrain1Factory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解决 grain 参 号
            }

            Assert.True(false, "Exception should have been raised");
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailDownCastToGrandchild()
        {
            // GeneratorTestDerivedGrain1Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedGrain2Reference 源自 GeneratorTestGrainReference
            // GeneratorTestDerivedDerivedGrainReference 源自 GeneratorTestDerivedGrain2Reference

            //GrainReference grain = GeneratorTestGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestGrain");
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedDerivedGrainFactory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解决 grain 参 号
            }

            Assert.True(false, "Exception should have been raised");
        }
#endif
        /// <summary>
        /// 测试铸造后从基界面继承的呼叫方法。
        /// 验证基础接口中界定的方法仍然可以使用
        /// 当使用衍生 grain 参考文献和铸造作业后的工作时。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task CastCallMethodInheritedFromBaseClass()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Task<bool> isNullStr;

            IGeneratorTestDerivedGrain1 grain = this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());
            isNullStr = grain.StringIsNullOrEmpty();
            Assert.True(await isNullStr, "Value should be null initially");

            isNullStr = grain.StringSet("a").ContinueWith((_) => grain.StringIsNullOrEmpty(), cancellationToken).Unwrap();
            Assert.False(await isNullStr, "Value should not be null after SetString(a)");

            isNullStr = grain.StringSet(null!).ContinueWith((_) => grain.StringIsNullOrEmpty(), cancellationToken).Unwrap();
            Assert.True(await isNullStr, "Value should be null after SetString(null)");

            IGeneratorTestGrain cast = grain.AsReference<IGeneratorTestGrain>();
            isNullStr = cast.StringSet("b").ContinueWith((_) => grain.StringIsNullOrEmpty(), cancellationToken).Unwrap();
            Assert.False(await isNullStr, "Value should not be null after cast.SetString(b)");
        }
    }
}

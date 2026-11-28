using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests
{
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Orleans Grain 引用转换能力的测试。
    /// 验证 Grain 引用可以在兼容接口之间安全转换，
    /// 包括向上转换为基接口、在多个已实现接口之间转换，
    /// 以及对无效转换的正确错误处理。测试激活前和激活后两种场景。
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
        /// 测试将 Grain 引用转换为相同接口类型的基本操作。
        /// 验证将 Grain 引用转换为其自身的接口类型能够正常工作，
        /// 并返回可赋给预期类型的引用。
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
        /// 测试同一 Grain 实现的多个接口之间的转换。
        /// 验证实现多个接口的 Grain 可以在这些接口之间转换，
        /// 并保持正确的类型关系。
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
        /// 测试具有多种功能的 Grain 的读取器与写入器接口之间的转换。
        /// 验证实现多个相关接口的 Grain 可通过不同的接口视图访问，
        /// 同时保持状态一致性。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task CastMultifacetRWReference()
        {
            int newValue = 3;

            IMultifacetWriter writer = this.GrainFactory.GetGrain<IMultifacetWriter>(1);
            // 此测试用例中没有 Wait

            IMultifacetReader reader = writer.AsReference<IMultifacetReader>();

            await writer.SetValue(newValue);

            Task<int> readAsync = reader.GetValue();
            int result = await readAsync;

            Assert.Equal(newValue, result);
        }

        /// <summary>
        /// 测试显式解析 Grain 后的接口转换。
        /// 验证转换前已解析 Grain 引用时的接口转换行为，
        /// 确保正确检查接口兼容性。
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
        /// 测试无效转换尝试是否引发适当的异常。
        /// 验证尝试将 Grain 引用转换为非 Grain 类型
        /// （例如 bool）时会引发 ArgumentException。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastFailInternalCastFromBadType()
        {
            var grain = this.GrainFactory.GetGrain<ISimpleGrain>(
                Random.Shared.Next(),
                SimpleGrain.SimpleGrainNamePrefix);

            // 尝试将 Grain 转换为非 Grain 类型应当失败。
            Assert.Throws<ArgumentException>(() => this.internalGrainFactory.Cast(grain, typeof(bool)));
        }

        /// <summary>
        /// 测试相同类型转换的内部转换机制。
        /// 验证将 Grain 转换为其自身接口类型会优化为
        /// 空操作，返回同一引用对象。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastInternalCastFromMyType()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<ISimpleGrain>(Random.Shared.Next(), SimpleGrain.SimpleGrainNamePrefix);

            // 此转换应为空操作，因为该接口与初始引用的接口完全匹配。
            IAddressable cast = grain.Cast<ISimpleGrain>();

            Assert.Same(grain, cast);
            Assert.IsAssignableFrom<ISimpleGrain>(cast);
        }

        /// <summary>
        /// 测试从派生 Grain 接口向上转换为基接口。
        /// 验证 Grain 引用可遵循正常继承规则，从派生接口
        /// 转换为其基接口。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastInternalCastUpFromChild()
        {
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());

            // 此转换应为空操作，因为初始引用的接口实现了此接口。
            IAddressable cast = grain.Cast<IGeneratorTestGrain>();

            Assert.Same(grain, cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);
        }

        /// <summary>
        /// 测试通过 AsReference 方法向上转换 Grain 引用。
        /// 验证派生 Grain 引用可以使用 AsReference API 向上转换为基接口，
        /// 并保持类型兼容性。
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
        /// 测试 Grain 解析后同级接口之间的横向转换是否失败。
        /// 验证当 Grain 已解析时，尝试在不相关接口（层次结构中的同级接口）
        /// 之间转换会引发 InvalidCastException。
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
        /// 测试无效横向转换尝试后的操作是否失败。
        /// 验证当实际 Grain 未实现相应接口时，在错误转换的 Grain 引用上
        /// 调用方法会引发 InvalidCastException。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task FailOperationAfterSideCast()
        {
            IGeneratorTestDerivedGrain1 grain = this.GrainFactory.GetGrain<IGeneratorTestDerivedGrain1>(GetRandomGrainId());

            // Grain 引用尚未解析时，转换以乐观方式成功
            IGeneratorTestDerivedGrain2 cast = grain.AsReference<IGeneratorTestDerivedGrain2>();

            // Grain 引用完全解析后，操作失败
            await Assert.ThrowsAsync<InvalidCastException>(() => cast.StringConcat("a", "b", "c"));
        }

        /// <summary>
        /// 测试带延续操作链的复杂转换场景。
        /// 验证在异步延续操作上下文中，无效转换能够适当地失败，
        /// 而共同接口上的有效操作仍然正常工作。
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

                        // 始终允许进行转换，因此此处应成功。
                        cast = grain.AsReference<IGeneratorTestDerivedGrain2>();
                    },
                    cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        // 调用 Grain 未实现的方法，导致转换失败。
                        Assert.True(t.IsCompletedSuccessfully);
                        return cast!.StringConcat("a", "b", "c");
                    },
                    cancellationToken)
                .Unwrap()
                .ContinueWith(
                    t =>
                    {
                        // 调用 Grain 已实现的共同接口上的方法。
                        // 此处不应引发异常。
                        Assert.True(t.IsFaulted);
                        return cast!.StringIsNullOrEmpty();
                    },
                    cancellationToken)
                .Unwrap();

            // 确保最后一个任务未引发异常。
            var av2Result = await av2;
            Assert.True(av2Result);
        }

        /// <summary>
        /// 测试经过多级接口继承向上转换。
        /// 验证 Grain 引用能够从孙级接口转换
        /// 为父接口和祖父接口，并进行正确的类型检查，
        /// 以防止跨层次结构的无效转换。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public void CastGrainRefUpCastFromGrandchild()
        {
            GrainReference cast;
            GrainReference grain = (GrainReference)this.GrainFactory.GetGrain<IGeneratorTestDerivedDerivedGrain>(GetRandomGrainId());

            // 父接口
            cast = (GrainReference)grain.AsReference<IGeneratorTestDerivedGrain2>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedDerivedGrain>(cast);
            Assert.IsAssignableFrom<IGeneratorTestDerivedGrain2>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);

            // 跨继承层次结构之外的转换不应成功
            Assert.False(cast is IGeneratorTestDerivedGrain1);

            // 祖父接口
            cast = (GrainReference)grain.AsReference<IGeneratorTestGrain>();
            Assert.IsAssignableFrom<IGeneratorTestDerivedDerivedGrain>(cast);
            Assert.IsAssignableFrom<IGeneratorTestGrain>(cast);

            // 跨继承层次结构之外的转换不应成功
            Assert.False(cast is IGeneratorTestDerivedGrain1);
        }

        /// <summary>
        /// 测试从深层嵌套派生接口向上转换。
        /// 验证从继承层次结构中深达多级的接口进行转换时，
        /// 类型关系正确。
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
        /// 测试对转换为自身接口的 Grain 引用执行异步操作。
        /// 验证将 Grain 引用转换为其自身接口类型之后，
        /// Grain 方法调用仍能正常工作。
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

        // todo: 实现白盒访问
#if TODO
        [Fact]
        public void CastAsyncGrainRefUpCastFromChild()
        {
            // GeneratorTestDerivedGrain1Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedGrain2Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedDerivedGrainReference 派生自 GeneratorTestDerivedGrain2Reference。

            //GrainReference grain = GeneratorTestDerivedGrain1Reference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "",
                "GeneratorTestGrain.GeneratorTestDerivedGrain1" );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestGrainFactory.Cast(grain);
            Assert.NotNull(cast);
            //Assert.Same(typeof(IGeneratorTestGrain), cast.GetType());

            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解析 Grain 引用
            }

            Assert.True(cast.IsResolved);
            Assert.True(grain.IsResolved);
        }

        [Fact]
        public void CastAsyncGrainRefUpCastFromGrandchild()
        {
            // GeneratorTestDerivedGrain1Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedGrain2Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedDerivedGrainReference 派生自 GeneratorTestDerivedGrain2Reference。

            //GrainReference grain = GeneratorTestDerivedDerivedGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestDerivedDerivedGrain"
            );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestGrainFactory.Cast(grain);
            Assert.NotNull(cast);
            //Assert.Same(typeof(IGeneratorTestGrain), cast.GetType());

            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解析 Grain 引用
            }

            Assert.True(cast.IsResolved);
            Assert.True(grain.IsResolved);
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailSideCastToPeer()
        {
            // GeneratorTestDerivedGrain1Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedGrain2Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedDerivedGrainReference 派生自 GeneratorTestDerivedGrain2Reference。

            //GrainReference grain = GeneratorTestDerivedGrain1Reference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestDerivedGrain1"
            );
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedGrain2Factory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解析 Grain 引用
            }

            Assert.True(false, "Exception should have been raised");
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailDownCastToChild()
        {
            // GeneratorTestDerivedGrain1Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedGrain2Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedDerivedGrainReference 派生自 GeneratorTestDerivedGrain2Reference。

            //GrainReference grain = GeneratorTestGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestGrain");
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedGrain1Factory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解析 Grain 引用
            }

            Assert.True(false, "Exception should have been raised");
        }

        [Fact]
        [ExpectedExceptionAttribute(typeof(InvalidCastException))]
        public void CastAsyncGrainRefFailDownCastToGrandchild()
        {
            // GeneratorTestDerivedGrain1Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedGrain2Reference 派生自 GeneratorTestGrainReference。
            // GeneratorTestDerivedDerivedGrainReference 派生自 GeneratorTestDerivedGrain2Reference。

            //GrainReference grain = GeneratorTestGrainReference.GetGrain(GetRandomGrainId());
            var lookupPromise = GrainReference.CreateGrain(
                "", 
                "GeneratorTestGrain.GeneratorTestGrain");
            GrainReference grain = new GrainReference(lookupPromise);

            GrainReference cast = (GrainReference) GeneratorTestDerivedDerivedGrainFactory.Cast(grain);
            if (!cast.IsResolved)
            {
                cast.Wait(100);  // 解析 Grain 引用
            }

            Assert.True(false, "Exception should have been raised");
        }
#endif
        /// <summary>
        /// 测试转换后调用从基接口继承的方法。
        /// 验证基接口中定义的方法在使用派生 Grain 引用及执行转换后，
        /// 仍然可以访问。
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

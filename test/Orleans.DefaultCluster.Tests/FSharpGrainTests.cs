using Microsoft.FSharp.Core;
using TestExtensions;
using UnitTests.FSharpTypes;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 试验 Orleans" F#类型和 grains.
    /// 证明 Orleans 能够正确地将F#结构序列/消除,如记录、选择和受歧视的工会,
    /// 确保 .NET 生态系统内适当的跨语言支持。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class FSharpGrainTests : HostedTestClusterEnsureDefaultStarted
    {
        public FSharpGrainTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        private async Task PingTest<T>(T input)
        {
            var id = Guid.NewGuid();
            var grain = this.GrainFactory.GetGrain<IGeneric1Argument<T>>(id, "UnitTests.Grains.Generic1ArgumentGrain");
            var output = await grain.Ping(input);
            Assert.Equal(input, output);
        }

        /// <summary>
        /// 测试原始F#型基本通用 grain 功能。
        /// 验证通用 grains 在C# 调用时能够处理简单类型参数。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp")]
        public async Task FSharpGrains_Ping()
        {
            await PingTest<int>(1);
        }

        /// <summary>
        /// F#记录类型基本测试序列化
        /// 验证 Orleans 能够适当地对含有原始值的F#记录进行序列/消除其子空间。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofInt()
        {
            await PingTest<UnitTests.FSharpTypes.Record>(UnitTests.FSharpTypes.Record.ofInt(1));
        }

        /// <summary>
        /// F# 测试记录序列化,没有 Orleans 属性。
        /// 此测试证明 Orleans有能力处理F#记录,即使这些记录缺乏
        /// 直线的可连序和不可变属性,验证无属性序列化。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_Some_WithNoAttributes()
        {
            await PingTest<RecordOfIntOptionWithNoAttributes>(RecordOfIntOptionWithNoAttributes.ofInt(1));
        }

        /// <summary>
        /// F# 测试记录带有明确 Orleans 属性的序列。
        /// 验证含有选项类型(某些案件)的F#记录可以适当序列化
        /// 当装饰时,具有可连串和不可改变的属性。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_Some()
        {
            await PingTest<RecordOfIntOption>(RecordOfIntOption.ofInt(1));
        }

        /// <summary>
        /// F# 测试记录序列,无选项值。
        /// 验证无一情况下F#选项类型的 Orleans处理情况,确保适当的无效处理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_None()
        {
            await PingTest<RecordOfIntOption>(RecordOfIntOption.Empty);
        }

        /// <summary>
        /// 测试F#类通用记录类型序列化。
        /// 验证 Orleans 能用类型参数处理 F# 记录,测试组合
        /// F# 通用和 Orleans' 序列系统。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofInt()
        {
            var input = GenericRecord<int>.ofT(0);
            await PingTest<GenericRecord<int>>(input);
        }

        /// <summary>
        /// 测试含有选项类型的通用F#记录序列(某些案例)。
        /// 这一复杂情景验证了 Orleans处理嵌套F#型结构的能力:
        /// 含有带有值的选项类型的通用记录。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofIntOption_Some()
        {
            var input = GenericRecord<FSharpOption<int>>.ofT(FSharpOption<int>.Some(0));
            await PingTest<GenericRecord<FSharpOption<int>>>(input);
        }

        /// <summary>
        /// 测试含有选项类型的通用F#记录(无一例)的序列化。
        /// 验证在通用F#记录结构内对无效值的适当处理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofIntOption_None()
        {
            var input = GenericRecord<FSharpOption<int>>.ofT(FSharpOption<int>.None);
            await PingTest<GenericRecord<FSharpOption<int>>>(input);
        }

        /// <summary>
        /// F#选项类型的直接序列化测试(某些案例)。
        /// 校验 Orleans 是否在F# 含有值时能够处理其内置选项类型。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_IntOption_Some()
        {
            var input = FSharpOption<int>.Some(10);
            await PingTest<FSharpOption<int>>(input);
        }

        /// <summary>
        /// F#选项类型的直接序列化测试(无例)。
        /// 验证 Orleans 正确处理 F# 无值, 表示没有数值。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_IntOption_None()
        {
            var input = FSharpOption<int>.None;
            await PingTest<FSharpOption<int>>(input);
        }
    }
}

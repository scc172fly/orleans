using Microsoft.FSharp.Core;
using TestExtensions;
using UnitTests.FSharpTypes;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 与 F# 类型及 Grain 的互操作性。
    /// 验证 Orleans 能否正确序列化及反序列化 F# 构造，例如记录、option 和可辨识联合，
    /// 确保 .NET 生态系统内具有正确的跨语言支持。
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
        /// 测试使用基本 F# 类型时的泛型 Grain 基础功能。
        /// 验证从 C# 调用时，泛型 Grain 能正确处理简单类型参数。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp")]
        public async Task FSharpGrains_Ping()
        {
            await PingTest<int>(1);
        }

        /// <summary>
        /// 测试基本 F# 记录类型的序列化。
        /// 验证 Orleans 能正确序列化及反序列化包含基本类型值的 F# 记录。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofInt()
        {
            await PingTest<UnitTests.FSharpTypes.Record>(UnitTests.FSharpTypes.Record.ofInt(1));
        }

        /// <summary>
        /// 测试不带 Orleans 特性的 F# 记录序列化。
        /// 此测试展示 Orleans 即使在 F# 记录没有
        /// 显式 Serializable 及 Immutable 特性时也能处理它们，验证无特性序列化。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_Some_WithNoAttributes()
        {
            await PingTest<RecordOfIntOptionWithNoAttributes>(RecordOfIntOptionWithNoAttributes.ofInt(1));
        }

        /// <summary>
        /// 测试带有显式 Orleans 特性的 F# 记录序列化。
        /// 验证包含 option 类型（Some 分支）的 F# 记录，
        /// 在使用 Serializable 及 Immutable 特性修饰时能够正确序列化。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_Some()
        {
            await PingTest<RecordOfIntOption>(RecordOfIntOption.ofInt(1));
        }

        /// <summary>
        /// 测试包含 None option 值的 F# 记录序列化。
        /// 验证 Orleans 在 None 分支中对 F# option 类型的处理，确保正确处理 null。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_Record_ofIntOption_None()
        {
            await PingTest<RecordOfIntOption>(RecordOfIntOption.Empty);
        }

        /// <summary>
        /// 测试泛型 F# 记录类型的序列化。
        /// 验证 Orleans 能处理带类型参数的 F# 记录，测试
        /// F# 泛型与 Orleans 序列化系统的组合。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofInt()
        {
            var input = GenericRecord<int>.ofT(0);
            await PingTest<GenericRecord<int>>(input);
        }

        /// <summary>
        /// 测试包含 option 类型（Some 分支）的泛型 F# 记录的序列化。
        /// 此复杂场景验证 Orleans 处理嵌套 F# 类型构造的能力：
        /// 包含有值的 option 类型的泛型记录。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofIntOption_Some()
        {
            var input = GenericRecord<FSharpOption<int>>.ofT(FSharpOption<int>.Some(0));
            await PingTest<GenericRecord<FSharpOption<int>>>(input);
        }

        /// <summary>
        /// 测试包含 option 类型（None 分支）的泛型 F# 记录的序列化。
        /// 验证泛型 F# 记录结构中的 null 值处理正确。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_GenericRecord_ofIntOption_None()
        {
            var input = GenericRecord<FSharpOption<int>>.ofT(FSharpOption<int>.None);
            await PingTest<GenericRecord<FSharpOption<int>>>(input);
        }

        /// <summary>
        /// 测试 F# option 类型（Some 分支）的直接序列化。
        /// 验证 Orleans 能够处理包含值的 F# 内置 option 类型。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_IntOption_Some()
        {
            var input = FSharpOption<int>.Some(10);
            await PingTest<FSharpOption<int>>(input);
        }

        /// <summary>
        /// 测试 F# option 类型（None 分支）的直接序列化。
        /// 验证 Orleans 能正确处理表示无值的 F# None 值。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Generics"), TestCategory("FSharp"), TestCategory("Serialization")]
        public async Task FSharpGrains_Ping_IntOption_None()
        {
            var input = FSharpOption<int>.None;
            await PingTest<FSharpOption<int>>(input);
        }
    }
}

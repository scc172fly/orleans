using System.Text;
using Orleans.Runtime;
using Orleans.Storage;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 关系型后端共用的存储键标识测试场景。被测存储应使用固定值
    /// 哈希器，以便根据键列而非哈希值区分数据行。
    /// </summary>
    internal static class GrainStorageKeyFormatTests
    {
        private const string StateName = "state";

        /// <summary>
        /// 不同类型的 Grain 即使共用键和状态名称，其状态也相互独立。
        /// </summary>
        public static async Task GrainTypesSharingKeyAreIsolated(IGrainStorage storage)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var typeA = GrainType.Create($"key-format-a-{suffix}");
            var typeB = GrainType.Create($"key-format-b-{suffix}");
            var guid = Guid.NewGuid();
            IdSpan[] keys =
            [
                GrainIdKeyExtensions.CreateIntegerKey(0),
                GrainIdKeyExtensions.CreateIntegerKey(42, "ext"),
                GrainIdKeyExtensions.CreateGuidKey(guid),
                GrainIdKeyExtensions.CreateGuidKey(guid, "ext"),
                IdSpan.Create("string-key"),
            ];

            foreach (var key in keys)
            {
                await AssertIsolated(storage, (GrainId.Create(typeA, key), StateName), (GrainId.Create(typeB, key), StateName));
            }
        }

        /// <summary>
        /// Grain 类型和状态名称即使拼接后得到相同文本、仅大小写不同，或不是
        /// 有效的 UTF-8，也具有独立的状态。
        /// </summary>
        public static async Task GrainTypeAndStateNameBoundariesAreIsolated(IGrainStorage storage)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var key = IdSpan.Create("shared-key");
            ((GrainType Type, string StateName) First, (GrainType Type, string StateName) Second)[] pairs =
            [
                ((GrainType.Create($"tenant-{suffix}/cart"), "state"), (GrainType.Create($"tenant-{suffix}"), "cart/state")),
                ((GrainType.Create($"case-{suffix}"), StateName), (GrainType.Create($"CASE-{suffix}"), StateName)),
                ((GrainType.Create($"state-case-{suffix}"), "state"), (GrainType.Create($"state-case-{suffix}"), "STATE")),
                ((new GrainType([.. Encoding.UTF8.GetBytes(suffix), 0x61, 0xFF]), StateName), (new GrainType([.. Encoding.UTF8.GetBytes(suffix), 0x61, 0xFE]), StateName)),
            ];

            foreach (var (first, second) in pairs)
            {
                await AssertIsolated(
                    storage,
                    (GrainId.Create(first.Type, key), first.StateName),
                    (GrainId.Create(second.Type, key), second.StateName));
            }
        }

        /// <summary>
        /// 同一 Grain 中名称不同的状态相互独立。
        /// </summary>
        public static async Task StateNamesOfOneGrainAreIsolated(IGrainStorage storage)
        {
            var grainId = GrainId.Create(GrainType.Create($"key-format-states-{Guid.NewGuid():N}"), GrainIdKeyExtensions.CreateIntegerKey(7));

            await AssertIsolated(storage, (grainId, "first"), (grainId, "second"));
        }

        /// <summary>
        /// 同一 Grain 类型的不同键即使解析为相同的整数键或 GUID 键，其状态也相互独立。
        /// </summary>
        public static async Task KeysParsingToSameValueAreIsolated(IGrainStorage storage)
        {
            var grainType = GrainType.Create($"key-format-keys-{Guid.NewGuid():N}");
            var guidA = Guid.NewGuid().ToString("N");
            var guidB = Guid.NewGuid().ToString("N");

            // 每个键仅出现一次：清除状态会保留数据行，因此不能再次将该键作为新状态写入。
            (string First, string Second)[] keyPairs =
            [
                ("20235", "020235"),
                ("ABC", "abc"),
                ("1+x", "01+x"),
                ("1", "1+"),
                (guidA, guidA.ToUpperInvariant()),
                (guidB, guidB + "+"),
                ("trailing", "trailing "),
                ("é", "e"),

                // 不同键在默认哈希器下具有相同哈希值。
                ("abcdeFghiJkLMNoPqrstuvwx", "AbcdefgHiJKlmnOpQRstuvwx"),
            ];

            foreach (var (first, second) in keyPairs)
            {
                await AssertIsolated(
                    storage,
                    (GrainId.Create(grainType, IdSpan.Create(first)), StateName),
                    (GrainId.Create(grainType, IdSpan.Create(second)), StateName));
            }
        }

        /// <summary>
        /// 同一 Grain 类型中不同种类的键即使其整数、GUID 和扩展
        /// 部分相同，状态也相互独立。
        /// </summary>
        public static async Task KeysOfDifferentKindsAreIsolated(IGrainStorage storage)
        {
            var grainType = GrainType.Create($"key-format-kinds-{Guid.NewGuid():N}");

            // 前八个字节为零的 GUID 与整数键具有相同的 N0/N1 分解结果。
            var guidBytes = new byte[16];
            BitConverter.GetBytes(5L).CopyTo(guidBytes, 8);
            var guidMatchingInteger = new Guid(guidBytes);

            (IdSpan First, IdSpan Second)[] keyPairs =
            [
                (IdSpan.Create("string-and-extension"), GrainIdKeyExtensions.CreateIntegerKey(0, "string-and-extension")),
                (IdSpan.Create("guid-extension"), GrainIdKeyExtensions.CreateGuidKey(Guid.Empty, "guid-extension")),
                (GrainIdKeyExtensions.CreateIntegerKey(5), GrainIdKeyExtensions.CreateGuidKey(guidMatchingInteger)),

                // 解码后产生相同替换文本的无效 UTF-8 字节。
                (new IdSpan([0x61, 0xFF]), new IdSpan([0x61, 0xFE])),
            ];

            foreach (var (first, second) in keyPairs)
            {
                await AssertIsolated(
                    storage,
                    (GrainId.Create(grainType, first), StateName),
                    (GrainId.Create(grainType, second), StateName));
            }
        }

        /// <summary>
        /// 在旧版键格式中，共用键的两个不同类型 Grain 也会共用同一数据行，因此第二个 Grain 的
        /// 首次写入会与第一个 Grain 的首次写入发生冲突。
        /// </summary>
        public static async Task LegacyFormatSharesRowAcrossGrainTypes(IGrainStorage storage)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var key = GrainIdKeyExtensions.CreateIntegerKey(0);
            var grainA = GrainId.Create(GrainType.Create($"key-format-legacy-a-{suffix}"), key);
            var grainB = GrainId.Create(GrainType.Create($"key-format-legacy-b-{suffix}"), key);

            var stateA = NewState("legacy-a");
            await storage.WriteStateAsync(StateName, grainA, stateA);

            var readB = new GrainState<TestState1> { State = new TestState1() };
            await storage.ReadStateAsync(StateName, grainB, readB);
            Assert.True(readB.RecordExists);
            Assert.Equal(stateA.State, readB.State);

            await Assert.ThrowsAsync<InconsistentStateException>(() => storage.WriteStateAsync(StateName, grainB, NewState("legacy-b")));
        }

        private static async Task AssertIsolated(IGrainStorage storage, (GrainId GrainId, string StateName) first, (GrainId GrainId, string StateName) second)
        {
            var firstState = NewState($"{first.GrainId}/{first.StateName}");
            var secondState = NewState($"{second.GrainId}/{second.StateName}");

            await storage.WriteStateAsync(first.StateName, first.GrainId, firstState);
            await storage.WriteStateAsync(second.StateName, second.GrainId, secondState);
            Assert.Equal("1", firstState.ETag);
            Assert.Equal("1", secondState.ETag);

            await AssertStored(storage, first, firstState.State);
            await AssertStored(storage, second, secondState.State);

            await storage.ClearStateAsync(first.StateName, first.GrainId, firstState);

            var clearedState = new GrainState<TestState1> { State = new TestState1() };
            await storage.ReadStateAsync(first.StateName, first.GrainId, clearedState);
            Assert.False(clearedState.RecordExists);
            Assert.Equal(new TestState1(), clearedState.State);
            await AssertStored(storage, second, secondState.State);
        }

        private static async Task AssertStored(IGrainStorage storage, (GrainId GrainId, string StateName) grain, TestState1? expected)
        {
            var readState = new GrainState<TestState1> { State = new TestState1() };
            await storage.ReadStateAsync(grain.StateName, grain.GrainId, readState);
            Assert.True(readState.RecordExists, $"No state was read for {grain.GrainId} ({grain.StateName}).");
            Assert.Equal("1", readState.ETag);
            Assert.Equal(expected, readState.State);
        }

        private static GrainState<TestState1> NewState(string value) => new() { State = new TestState1 { A = value, B = 1, C = 2 } };
    }
}

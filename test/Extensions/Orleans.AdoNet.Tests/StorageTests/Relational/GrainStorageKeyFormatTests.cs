using System.Text;
using Orleans.Runtime;
using Orleans.Storage;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 存储关键身份假设情景,由关系后端共享。
    /// 以便用关键列而不是散列值来区分行。
    /// </summary>
    internal static class GrainStorageKeyFormatTests
    {
        private const string StateName = "state";

        /// <summary>
        /// 具有同一密钥和国名的不同类型Grains 有单独状态。
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
        /// Grain 类型和国名与同一文本相联,只在字母情况下不同,或没有
        /// 有效的 UTF-8 有单独的状态。
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
        /// 在一个 grain 上有不同名称的国家有单独国名。
        /// </summary>
        public static async Task StateNamesOfOneGrainAreIsolated(IGrainStorage storage)
        {
            var grainId = GrainId.Create(GrainType.Create($"key-format-states-{Guid.NewGuid():N}"), GrainIdKeyExtensions.CreateIntegerKey(7));

            await AssertIsolated(storage, (grainId, "first"), (grainId, "second"));
        }

        /// <summary>
        /// 独特 grain 键的一键 grain 类型,如果向相同整数或图形用户界面D 键分析,则有独立的状态。
        /// </summary>
        public static async Task KeysParsingToSameValueAreIsolated(IGrainStorage storage)
        {
            var grainType = GrainType.Create($"key-format-keys-{Guid.NewGuid():N}");
            var guidA = Guid.NewGuid().ToString("N");
            var guidB = Guid.NewGuid().ToString("N");

            // 每个键都显示一次: 清除会保留行, 所以无法再次写入一个密钥作为新状态 。
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

                // 不同的键, 等同默认的杂交。
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
        /// 一种 grain 类型上不同类型键的不同键有独立的状态,即使它们的整数、图形界面D和扩展
        /// 部件重合。
        /// </summary>
        public static async Task KeysOfDifferentKindsAreIsolated(IGrainStorage storage)
        {
            var grainType = GrainType.Create($"key-format-kinds-{Guid.NewGuid():N}");

            // 头八个字节为零的图形用户界面D的分解与整数键相同。
            var guidBytes = new byte[16];
            BitConverter.GetBytes(5L).CopyTo(guidBytes, 8);
            var guidMatchingInteger = new Guid(guidBytes);

            (IdSpan First, IdSpan Second)[] keyPairs =
            [
                (IdSpan.Create("string-and-extension"), GrainIdKeyExtensions.CreateIntegerKey(0, "string-and-extension")),
                (IdSpan.Create("guid-extension"), GrainIdKeyExtensions.CreateGuidKey(Guid.Empty, "guid-extension")),
                (GrainIdKeyExtensions.CreateIntegerKey(5), GrainIdKeyExtensions.CreateGuidKey(guidMatchingInteger)),

                // 无效的 UTF-8 字节, 用来解码到相同的替换文本 。
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
        /// 不同类型两种 grains ,其中共用一个键的,在遗留关键键格式中也共享一行,因此第二个
        /// 第一次写作与第一个冲突 。
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

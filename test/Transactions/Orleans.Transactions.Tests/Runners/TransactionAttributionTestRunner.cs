using Orleans.Transactions.TestKit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    public abstract class TransactionAttributionTestRunner
    {
        protected readonly IGrainFactory grainFactory;
        protected readonly ITestOutputHelper output;

        protected TransactionAttributionTestRunner(IGrainFactory grainFactory, ITestOutputHelper output)
        {
            this.output = output;
            this.grainFactory = grainFactory;
        }

        /// <summary>
        /// 测试可从非事务调用中调用的所有事务特性，确保其行为正确。
        /// 这些特性包括：
        /// 无特性：应无事务上下文。
        /// Suppress：应无事务上下文。
        /// CreateOrJoin：应具有独立的事务上下文。
        /// Create：应具有独立的事务上下文。
        /// Supported：应无事务上下文。
        /// NotAllowed：应无事务上下文。
        /// </summary>
        [Fact]
        public async Task AllSupportedAttributesFromOutsideTransactionTest()
        {
            ITransactionAttributionGrain top = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid());
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid()),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Suppress),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Supported),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.NotAllowed)
                    })
            };

            List<string?>?[] results = await top.GetNestedTransactionIds(0, tiers);
            this.WriteResults(results);

            // 确保有两个层级。
            Assert.Equal(2, results.Length);

            // 确保顶层调用没有 transactionId。
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            Assert.Single(topTransactionIds);
            Assert.Null(topTransactionIds.First());

            // 检查子调用的 transactionId，应依次为 null、null、guid1、guid2、null、null。
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(6, subcallTransactionIds.Count);

            Assert.Null(subcallTransactionIds[0]); // 无特性。

            Assert.Null(subcallTransactionIds[1]); // Suppress 特性。

            Assert.NotNull(subcallTransactionIds[2]); // CreateOrJoin 特性。

            Assert.NotNull(subcallTransactionIds[3]); // Create 新事务特性。
            // 确保 Required 和 RequiredNew 调用的事务 ID 不同。
            Assert.NotEqual(subcallTransactionIds[2], subcallTransactionIds[3]);

            Assert.Null(subcallTransactionIds[4]); // Supported 特性。

            Assert.Null(subcallTransactionIds[5]); // NotAllowed 特性。
        }

        /// <summary>
        /// 测试可从事务调用内部调用的所有事务特性，确保其行为正确。
        /// 这些特性包括：
        /// 无特性：应无事务上下文。
        /// Suppress：应无事务上下文。
        /// CreateOrJoin：应与父调用共享事务上下文。
        /// Create：应具有独立的事务上下文。
        /// Join：应与父调用共享事务上下文。
        /// Supported：应与父调用共享事务上下文。
        /// </summary>
        [Fact]
        public async Task AllSupportedAttributesFromWithinTransactionTest()
        {
            ITransactionAttributionGrain top = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create);
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid()),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Suppress),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Join),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Supported)
                    })
            };

            List<string?>?[] results = await top.GetNestedTransactionIds(0, tiers);
            this.WriteResults(results);

            // 确保有两个层级。
            Assert.Equal(2, results.Length);

            // 确保顶层调用具有 transactionId。
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 检查子调用的 transactionId，应依次为 null、null、guid1、guid2，其中 guid1 应与顶层调用 ID 一致。
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(6, subcallTransactionIds.Count);

            Assert.Null(subcallTransactionIds[0]); // 无特性。

            Assert.Null(subcallTransactionIds[1]); // Suppress supported 特性。

            Assert.NotNull(subcallTransactionIds[2]); // CreateOrJoin 特性。
            // 确保标记为 required 的调用的 transactionId 与顶层调用一致。
            Assert.Equal(subcallTransactionIds[2], topTransactionId);

            Assert.NotNull(subcallTransactionIds[3]); // Create 特性。
            // 确保事务 ID 不同。
            Assert.NotEqual(subcallTransactionIds[2], subcallTransactionIds[3]);

            Assert.NotNull(subcallTransactionIds[4]); // Join 特性。
            // 确保标记为 Join 的调用的 transactionId 与顶层调用一致。
            Assert.Equal(subcallTransactionIds[4], topTransactionId);

            Assert.NotNull(subcallTransactionIds[5]); // Supported 特性。
            // 确保标记为 Supported 的调用的 transactionId 与顶层调用一致。
            Assert.Equal(subcallTransactionIds[5], topTransactionId);
        }

        [Fact]
        public async Task CreateOrJoinTest()
        {
            ITransactionAttributionGrain top = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin);
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin),
                    })
            };

            List<string?>?[] results = await top.GetNestedTransactionIds(0, tiers);
            this.WriteResults(results);

            // 确保有两个层级。
            Assert.Equal(2, results.Length);

            // 确保顶层调用具有 transactionId。
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 检查子调用的 transactionId，应依次为 null、null、guid1、guid1，其中 guid1 应与顶层调用 ID 一致。
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(2, subcallTransactionIds.Count);
            Assert.NotNull(subcallTransactionIds[0]);
            Assert.NotNull(subcallTransactionIds[1]);
            // 确保标记为 required 的调用的 transactionId 与父调用一致。
            Assert.Equal(subcallTransactionIds[0], topTransactionId);
            // 确保标记为 required 的调用的 transactionId 与父调用一致。
            Assert.Equal(subcallTransactionIds[1], topTransactionId);
        }

        [Fact]
        public async Task CreateTest()
        {
            ITransactionAttributionGrain top = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create);
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create),
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create),
                    })
            };

            List<string?>?[] results = await top.GetNestedTransactionIds(0, tiers);
            this.WriteResults(results);

            // 确保有两个层级。
            Assert.Equal(2, results.Length);

            // 确保顶层调用具有 transactionId。
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 检查子调用的 transactionId，应依次为 null、null、guid1、guid2，其中 guid1 和 guid2 应互不相同，且均与顶层 transactionId 不同。
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(2, subcallTransactionIds.Count);
            Assert.NotNull(subcallTransactionIds[0]);
            Assert.NotNull(subcallTransactionIds[1]);
            // 确保标记为 RequiresNew 的调用的 transactionId 与父调用不同。
            Assert.NotEqual(subcallTransactionIds[0], topTransactionId);
            // 确保标记为 RequiresNew 的调用的 transactionId 与父调用不同。
            Assert.NotEqual(subcallTransactionIds[1], topTransactionId);
            // 确保事务 ID 不同。
            Assert.NotEqual(subcallTransactionIds[0], subcallTransactionIds[1]);
        }

        /// <summary>
        /// 检查标记为 'supported' 的调用中的事务能否加入父事务。
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task SupportedTest()
        {
            ITransactionAttributionGrain top = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create);
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Supported),
                    }),
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.CreateOrJoin),
                    })
            };

            List<string?>?[] results = await top.GetNestedTransactionIds(0, tiers);
            this.WriteResults(results);

            // 确保共有三层调用。
            Assert.Equal(3, results.Length);

            // 确保顶层调用具有 transactionId。
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 检查第一层调用的 transactionId，应为与父调用一致的 GUID。
            List<string?>? tier1callTransactionIds = results[1];
            Assert.NotNull(tier1callTransactionIds);
            string? tier1callTransactionId = Assert.Single(tier1callTransactionIds);
            Assert.NotNull(tier1callTransactionId);
            // 确保标记为 Supported 的调用的 transactionId 与父调用一致。
            Assert.Equal(tier1callTransactionId, topTransactionId);

            // 检查第二层调用的 transactionId，应为与父调用一致的 GUID。
            List<string?>? tier2callTransactionIds = results[2];
            Assert.NotNull(tier2callTransactionIds);
            string? tier2callTransactionId = Assert.Single(tier2callTransactionIds);
            Assert.NotNull(tier2callTransactionId);
            // 确保标记为 CreateOrJoin 的调用的 transactionId 与父调用一致。
            Assert.Equal(tier2callTransactionId, topTransactionId);
        }

        [Fact]
        public async Task JoinFailTest()
        {
            ITransactionAttributionGrain fail = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid());
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Join),
                    })
            };

            await Assert.ThrowsAsync<NotSupportedException>(() => fail.GetNestedTransactionIds(0, tiers));
        }

        [Fact]
        public async Task NotAllowedFailTest()
        {
            ITransactionAttributionGrain fail = this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.Create);
            List<ITransactionAttributionGrain>[] tiers =
            {
                new List<ITransactionAttributionGrain>(new[] {
                    this.grainFactory.GetTransactionAttributionGrain(Guid.NewGuid(), TransactionOption.NotAllowed),
                    })
            };

            OrleansTransactionAbortedException exception = await Assert.ThrowsAsync<OrleansTransactionAbortedException>(() => fail.GetNestedTransactionIds(0, tiers));
            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        private void WriteResults(List<string?>?[] results)
        {
            for (var i = 0; i < results.Length; i++)
            {
                var formattedIds = results[i] is { } transactionIds
                    ? string.Join(",", transactionIds.Select(transactionId => transactionId ?? "<null id>"))
                    : "<null tier>";
                this.output.WriteLine($"{i} => {formattedIds}");
            }
        }
    }
}

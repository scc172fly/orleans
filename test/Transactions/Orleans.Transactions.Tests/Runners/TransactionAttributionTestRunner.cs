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
        /// 测试所有从非交易调用中调用的属性。 请确保它们都行为正确 。
        /// 其中包括:
        /// 属性 - 不应有交易背景
        /// - 不应有交易背景的禁止;
        /// CreateOrJoin - 应当具有独特的交易背景
        /// - 创建 - 应当具有独特的交易背景
        /// 支持 - 不应有交易背景
        /// NotAllowed - 不应有交易背景
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

            // 确定有两层
            Assert.Equal(2, results.Length);

            // 确保顶级调用没有 transactionId
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            Assert.Single(topTransactionIds);
            Assert.Null(topTransactionIds.First());

            // 复选分机调用 transactionIds,应该是无效、无效、公号、guid1、guid2、null、null
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(6, subcallTransactionIds.Count);

            Assert.Null(subcallTransactionIds[0]); // 无属性

            Assert.Null(subcallTransactionIds[1]); // 抑制属性

            Assert.NotNull(subcallTransactionIds[2]); // CreateOrJoin 属性

            Assert.NotNull(subcallTransactionIds[3]); // 创建新属性
            // 确保交易编号为需要的交易编号, RequiredNew 调用方式不同
            Assert.NotEqual(subcallTransactionIds[2], subcallTransactionIds[3]);

            Assert.Null(subcallTransactionIds[4]); // 支持的属性

            Assert.Null(subcallTransactionIds[5]); // NotAllowed 属性
        }

        /// <summary>
        /// 测试交易调用中可以调用的所有属性。 请确保它们都行为正确 。
        /// 其中包括:
        /// 属性 - 不应有交易背景
        /// - 不应有交易背景的禁止;
        /// CreateOrJoin - 与母公司应具有相同的交易环境
        /// - 创建 - 应当具有独特的交易背景
        /// - 加入应具有与母公司相同的交易环境
        /// 支持 - 应当与母公司有相同的交易环境
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

            // 确定有两层
            Assert.Equal(2, results.Length);

            // 确定最高一级调用有 transactionId
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 选中子复选 子调 transactionIds, 应该是无效、 空、 guid1、 guid2, 此处guid1 与从上对齐 Id
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(6, subcallTransactionIds.Count);

            Assert.Null(subcallTransactionIds[0]); // 无属性

            Assert.Null(subcallTransactionIds[1]); // 支持的后设封积属性

            Assert.NotNull(subcallTransactionIds[2]); // CreateOrJoin 属性
            // 确定需要的指定属性 transactionId 匹配顶部
            Assert.Equal(subcallTransactionIds[2], topTransactionId);

            Assert.NotNull(subcallTransactionIds[3]); // 创建属性
            // 确保交易id 的差别
            Assert.NotEqual(subcallTransactionIds[2], subcallTransactionIds[3]);

            Assert.NotNull(subcallTransactionIds[4]); // 附加属性
            // 确保 JIL 属性 transactionId 匹配上
            Assert.Equal(subcallTransactionIds[4], topTransactionId);

            Assert.NotNull(subcallTransactionIds[5]); // 支持的属性
            // 确定支持的属性 transactionId 匹配顶部
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

            // 确定有两层
            Assert.Equal(2, results.Length);

            // 确定最高一级调用有 transactionId
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 选中子调用 transactionIds, 应该是无效、 空、 guid1、 guid1、 guid1, 此处guid1 与从上到下匹配 Id
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(2, subcallTransactionIds.Count);
            Assert.NotNull(subcallTransactionIds[0]);
            Assert.NotNull(subcallTransactionIds[1]);
            // 确定所需 transactionId 匹配父对父
            Assert.Equal(subcallTransactionIds[0], topTransactionId);
            // 确定所需 transactionId 匹配父对父
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

            // 确定有两层
            Assert.Equal(2, results.Length);

            // 确定最高一级调用有 transactionId
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 复选分机调 transactionIds,应该是空、空、guid1、guid2,其中guid1和guid2应该不同,而不是顶级 transactionId
            List<string?>? subcallTransactionIds = results[1];
            Assert.NotNull(subcallTransactionIds);
            Assert.Equal(2, subcallTransactionIds.Count);
            Assert.NotNull(subcallTransactionIds[0]);
            Assert.NotNull(subcallTransactionIds[1]);
            // 确保给 RequiresNew 的 transactionId 分数与父母不匹配
            Assert.NotEqual(subcallTransactionIds[0], topTransactionId);
            // 确保给 RequiresNew 的 transactionId 分数与父母不匹配
            Assert.NotEqual(subcallTransactionIds[1], topTransactionId);
            // 确保交易id 的差别
            Assert.NotEqual(subcallTransactionIds[0], subcallTransactionIds[1]);
        }

        /// <summary>
        /// 检查是否在标为“ 支持” 的呼叫中进行交易, 以查看该交易是否加入母交易
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

            // 确定三层
            Assert.Equal(3, results.Length);

            // 确定最高一级调用有 transactionId
            List<string?>? topTransactionIds = results[0];
            Assert.NotNull(topTransactionIds);
            string? topTransactionId = Assert.Single(topTransactionIds);
            Assert.NotNull(topTransactionId);

            // 检查第一个第一级调数 transactionIds,应该是一个与父对齐的首级
            List<string?>? tier1callTransactionIds = results[1];
            Assert.NotNull(tier1callTransactionIds);
            string? tier1callTransactionId = Assert.Single(tier1callTransactionIds);
            Assert.NotNull(tier1callTransactionId);
            // 确定支持的属性 transactionId 匹配父母
            Assert.Equal(tier1callTransactionId, topTransactionId);

            // 复选第二级调用 transactionIds,应该是一个与父对齐的首饰
            List<string?>? tier2callTransactionIds = results[2];
            Assert.NotNull(tier2callTransactionIds);
            string? tier2callTransactionId = Assert.Single(tier2callTransactionIds);
            Assert.NotNull(tier2callTransactionId);
            // 确保 CreateOrJoin 的分 transactionId 匹配父母
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

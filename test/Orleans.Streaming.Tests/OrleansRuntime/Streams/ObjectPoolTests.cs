using Orleans.Providers.Streams.Common;
using Xunit;

namespace UnitTests.OrleansRuntime.Streams
{
    /// <summary>
    /// 目标集合分配、回收利用和资源管理测试。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Streaming")]
    public class ObjectPoolTests
    {
        private class Accumulator
        {
            public int CurrentlyAllocated { get; set; }
            public int MaxAllocated { get; set; }

        }
        private class TestPooledResource : PooledResource<TestPooledResource>
        {
            private readonly Accumulator accumulator;

            public int AllocationCount { get; private set; }

            public TestPooledResource(Accumulator accumulator)
            {
                this.accumulator = accumulator;
                this.accumulator.CurrentlyAllocated++;
                this.accumulator.MaxAllocated = Math.Max(this.accumulator.MaxAllocated, this.accumulator.CurrentlyAllocated);
            }

            public override void OnResetState()
            {
                accumulator.CurrentlyAllocated--;
                AllocationCount++;
            }
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void Alloc1Free1Test()
        {
            var accumulator = new Accumulator();
            IObjectPool<TestPooledResource> pool = new ObjectPool<TestPooledResource>(() => new TestPooledResource(accumulator));
            // 分配和免费10项
            for (int i = 0; i < 10; i++)
            {
                TestPooledResource resource = pool.Allocate();
                resource.Dispose();
            }
            // 分配的时间只一次一个,所以最大分配时间应为1
            Assert.Equal(1, accumulator.MaxAllocated);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void Alloc10Free1Test()
        {
            var accumulator = new Accumulator();
            IObjectPool<TestPooledResource> pool = new ObjectPool<TestPooledResource>(() => new TestPooledResource(accumulator));

            // 分配10项
            var resources = Enumerable.Range(0, 10).Select(i => pool.Allocate()).ToList();

            // 空闲 10
            resources.ForEach(r => r.Dispose());

            // 预先人口集中的池中10人,分配的最大数应为10
            Assert.Equal(10, accumulator.MaxAllocated);

            // 分配和免费10项
            for (int i = 0; i < 10; i++)
            {
                TestPooledResource resource = pool.Allocate();
                resource.Dispose();
            }

            // 一次只分配一个,但池子预先容纳了10人,所以最多分配10人
            Assert.Equal(10, accumulator.MaxAllocated);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void ReuseResourceTest()
        {
            const int WorkngSet = 20;
            var accumulator = new Accumulator();
            IObjectPool<TestPooledResource> pool = new ObjectPool<TestPooledResource>(() => new TestPooledResource(accumulator));

            for (int i = 0; i < 5; i++)
            {
                // 分配 WorkngSet 项目
                var resources = Enumerable.Range(0, WorkngSet).Select(v => pool.Allocate()).ToList();

                resources.Reverse(); // e. 处理时维持秩序

                // 空 WorkngSet
                resources.ForEach(r => r.Dispose());

                // 预 WorkngSet,因此最大分配量应为10
                Assert.Equal(WorkngSet, accumulator.MaxAllocated);

                // 分配和免费5项
                for (int j = 0; j < 5; j++)
                {
                    TestPooledResource resource = pool.Allocate();
                    int expectedAllocationCount = (i * (5 + 1)) // 先前循环中累积的分配款
                                                  + (j + 1); // 在此循环中累积的批量
                    Assert.Equal(expectedAllocationCount, resource.AllocationCount);
                    resource.Dispose();
                }

                // 一次只分配1个,但池储预先 WorkngSet,所以最多分配10个
                Assert.Equal(WorkngSet, accumulator.MaxAllocated);
            }
        }

    }
}

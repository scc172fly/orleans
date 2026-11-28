using Orleans.Providers.Streams.Common;
using Xunit;

namespace UnitTests.OrleansRuntime.Streams
{
    /// <summary>
    /// 对象池分配、回收及资源管理测试。
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
            // 分配并释放 10 个对象
            for (int i = 0; i < 10; i++)
            {
                TestPooledResource resource = pool.Allocate();
                resource.Dispose();
            }
            // 每次仅分配 1 个对象，因此最大分配数量应为 1
            Assert.Equal(1, accumulator.MaxAllocated);
        }

        [Fact, TestCategory("BVT"), TestCategory("Streaming")]
        public void Alloc10Free1Test()
        {
            var accumulator = new Accumulator();
            IObjectPool<TestPooledResource> pool = new ObjectPool<TestPooledResource>(() => new TestPooledResource(accumulator));

            // 分配 10 个对象
            var resources = Enumerable.Range(0, 10).Select(i => pool.Allocate()).ToList();

            // 释放 10 个对象
            resources.ForEach(r => r.Dispose());

            // 池已预先填充 10 个对象，因此最大分配数量应为 10
            Assert.Equal(10, accumulator.MaxAllocated);

            // 分配并释放 10 个对象
            for (int i = 0; i < 10; i++)
            {
                TestPooledResource resource = pool.Allocate();
                resource.Dispose();
            }

            // 每次仅分配 1 个对象，但池已预先填充 10 个对象，因此最大分配数量应为 10
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
                // 分配 WorkngSet 个对象
                var resources = Enumerable.Range(0, WorkngSet).Select(v => pool.Allocate()).ToList();

                resources.Reverse(); // 反转已分配对象的顺序，以在释放时保持顺序

                // 释放 WorkngSet 个对象
                resources.ForEach(r => r.Dispose());

                // 池已预先填充 WorkngSet 个对象，因此最大分配数量应为 10（原文如此，断言实际使用 WorkngSet）。
                Assert.Equal(WorkngSet, accumulator.MaxAllocated);

                // 分配并释放 5 个对象
                for (int j = 0; j < 5; j++)
                {
                    TestPooledResource resource = pool.Allocate();
                    int expectedAllocationCount = (i * (5 + 1)) // 之前各循环累计的分配次数
                                                  + (j + 1); // 当前循环累计的分配次数
                    Assert.Equal(expectedAllocationCount, resource.AllocationCount);
                    resource.Dispose();
                }

                // 每次仅分配 1 个对象，但池已预先填充 WorkngSet 个对象，因此最大分配数量应为 10（原文如此，断言实际使用 WorkngSet）。
                Assert.Equal(WorkngSet, accumulator.MaxAllocated);
            }
        }

    }
}

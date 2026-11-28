using System.Collections.Concurrent;
using System.Threading.Channels;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    public class CallChainReentrancyTestHelper
    {
        public BaseTestClusterFixture Fixture { get; set; } = null!;
        public int NumIterations { get; set; }

        // 2 个 Silo，循环遍历所有情况（使所有 Grain 都覆盖本地和远程场景）：
        //      非可重入 A, B, C
        //      可重入 X
        // 1）无死锁 A, A。
        // 2）无死锁 A, B, A。
        // 3）无死锁 X, A, X, A。
        // 4）无死锁 X, X。
        // 5）无死锁 X, A, X。
        // 6）无死锁 A, B, C, A。

        // 1）允许重入 A, A。
        public async Task CallChainReentrancy_1()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockNonReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId, true),
                    (grainId, true)
                };
                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        // 2）允许非可重入 Grain 上的重入 A, B, A。
        public async Task CallChainReentrancy_2()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockNonReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId, true),
                    (grainId + 100, true),
                    (grainId, true)
                };
                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        // 3）允许重入 X, A, X, A。
        public async Task CallChainReentrancy_3()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId + 1000, false),
                    (grainId, true),
                    (grainId + 1000, false),
                    (grainId, true)
                };

                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        // 4）无死锁 X, X。
        public async Task CallChainReentrancy_4()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId + 1000, false),
                    (grainId + 1000, false)
                };

                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        // 5）无死锁 X, A, X。
        public async Task CallChainReentrancy_5()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId + 1000, false),
                    (grainId, true),
                    (grainId + 1000, false)
                };

                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        // 6）仅在使用完整调用链重入时允许非可重入 Grain 上的重入 A, B, C, A。
        public async Task CallChainReentrancy_6()
        {
            long baseGrainId = Random.Shared.Next();
            for (var i = 0; i < NumIterations; i++)
            {
                var grainId = baseGrainId + i;
                var firstGrain = Fixture.GrainFactory.GetGrain<IDeadlockNonReentrantGrain>(grainId);
                var callChain = new List<(long GrainId, bool Blocking)>
                {
                    (grainId, true),
                    (grainId + 100, true),
                    (grainId + 200, true),
                    (grainId, true)
                };
                await firstGrain.CallNext_1(callChain, 1);
            }
        }

        /// <summary>
        /// 在可重入调用链内禁止调用链重入，以确保后续调用链不可重入。
        /// </summary>
        public async Task CallChainReentrancy_WithSuppression()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var aId = $"A-{Random.Shared.Next()}";
            var bId = $"B-{Random.Shared.Next()}";
            var a = Fixture.GrainFactory.GetGrain<ICallChainReentrancyGrain>(aId);

            var chain = new List<(string, ReentrancyCallType)>
            {
                // 1. a->a：启动新的调用链，允许重入 'a'。
                (aId, ReentrancyCallType.AllowCallChainReentrancy),

                // 2. a->b：不允许重入 'b'，因为 'b' 未显式请求重入。
                (bId, ReentrancyCallType.Regular),

                // 3. b->a：确保允许重入 'a'。
                (aId, ReentrancyCallType.Regular),

                // 4. a->b：禁止从 'a' 到 'b' 的调用链重入。由于 'b' 已在等待，对 'b' 的调用应阻塞（直到稍后显式解除阻塞）。
                (bId, ReentrancyCallType.SuppressCallChainReentrancy),
            };

            var observer = new CallChainObserver();
            var observerRef = Fixture.GrainFactory.CreateObjectReference<ICallChainObserver>(observer);

            var aTask = a.CallChain(observerRef, chain, 0, cts.Token);

            // 等待 'a' 收到来自 'b' 的调用。
            await observer.WaitForOperationAsync(CallChainOperation.Enter, aId, 3, cts.Token);

            // 通知 'a' 停止等待 'b' 返回，使最终调用（步骤 4）能够进入 'b' 并完成调用链。
            await a.UnblockWaiters(cts.Token);

            await aTask;

            // 等待 'b' 完成最终调用。
            await observer.WaitForOperationAsync(CallChainOperation.Exit, bId, 4, cts.Token);
        }

        public enum CallChainOperation
        {
            Enter,
            Exit,
        }

        public class CallChainObserver : ICallChainObserver
        {
            private readonly Channel<(CallChainOperation Operation, string Grain, int CallIndex)> _operations = Channel.CreateUnbounded<(CallChainOperation Operation, string Grain, int CallIndex)>();
            private readonly ConcurrentBag<(CallChainOperation Operation, string Grain, int CallIndex)> _allOperations = new();

            public async Task OnEnter(string grain, int callIndex)
            {
                await _operations.Writer.WriteAsync((CallChainOperation.Enter, grain, callIndex));
            }

            public async Task OnExit(string grain, int callIndex)
            {
                await _operations.Writer.WriteAsync((CallChainOperation.Exit, grain, callIndex));
            }

            public async Task WaitForOperationAsync(CallChainOperation operationType, string grain, int callIndex, CancellationToken cancellationToken = default)
            {
                // 首先检查是否已观察到此操作。
                if (_allOperations.Any(op => op.Operation == operationType && op.Grain.Equals(grain, StringComparison.Ordinal) && op.CallIndex == callIndex))
                {
                    return;
                }

                var operations = _operations.Reader;
                while (await operations.WaitToReadAsync(cancellationToken))
                {
                    Assert.True(operations.TryRead(out var operation));
                    _allOperations.Add(operation);

                    if (operation.Operation == operationType && operation.Grain.Equals(grain, StringComparison.Ordinal) && operation.CallIndex == callIndex)
                    {
                        return;
                    }

                    // 检查是否已超过预期调用索引。
                    if (operation.CallIndex > callIndex)
                    {
                        break;
                    }
                }

                Assert.Fail($"Expected to find operation ({operationType}, {grain}, {callIndex}) in {string.Join(", ", _allOperations.Select(op => $"({op.Operation}, {op.Grain}, {op.CallIndex})"))}");
            }
        }
    }
}

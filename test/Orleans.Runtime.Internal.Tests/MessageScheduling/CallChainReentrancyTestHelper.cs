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

        // 2 silos,环绕所有案件(迫使所有 grains 都本地和远程):
        //      非 Reentrant A、B、C
        //      重新entrant X
        // 1) 无死锁A, A
        // (2) 无死锁A、B、A
        // (3) 无死锁X、A、X、A
        // (4) 无死锁X, X
        // (5) 无死锁X、A、X
        // 6) 无死锁A、B、C、A

        // (1) 允许保留A、A
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

        // (2) 允许非再生 grains A、B、A
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

        // (3) 允许再充电 X、A、X、A
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

        // (4) 无死锁X, X
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

        // (5) 无死锁X、A、X
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

        // 6) 只有在使用全链回存 A、B、C、A时,才允许非再生 grains 重新注入
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
        /// 抑制呼唤链在静脉呼叫链中的再回流,以确保随后的呼唤链不再回流。
        /// </summary>
        public async Task CallChainReentrancy_WithSuppression()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

            var aId = $"A-{Random.Shared.Next()}";
            var bId = $"B-{Random.Shared.Next()}";
            var a = Fixture.GrainFactory.GetGrain<ICallChainReentrancyGrain>(aId);

            var chain = new List<(string, ReentrancyCallType)>
            {
                // 1. a->a: 启动一个新的呼叫链,允许再回录到“ a”
                (aId, ReentrancyCallType.AllowCallChainReentrancy),

                // 2. a- >b: 不允许重新输入“ b” , 因为“ b” 没有提出明确要求。
                (bId, ReentrancyCallType.Regular),

                // 3.b->a:确保允许重新输入“a”。
                (aId, ReentrancyCallType.Regular),

                // 4. a- >b: 禁止调用链从“ a” 到“ b”的重复。 “ b” 调用应屏蔽(直到稍后明确解开),因为“ b” 已经等待了 。
                (bId, ReentrancyCallType.SuppressCallChainReentrancy),
            };

            var observer = new CallChainObserver();
            var observerRef = Fixture.GrainFactory.CreateObjectReference<ICallChainObserver>(observer);

            var aTask = a.CallChain(observerRef, chain, 0, cts.Token);

            // 等待"a"接到b的电话
            await observer.WaitForOperationAsync(CallChainOperation.Enter, aId, 3, cts.Token);

            // 告诉'a' 停止等待 'b' 返回, 让最后的呼叫( 第4步)进入`b' 并完成呼叫链 。
            await a.UnblockWaiters(cts.Token);

            await aTask;

            // 等待“ b” 完成最后呼叫
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
                // 第一次检查,如果我们已经看过这个手术
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

                    // 检查我们是否通过了预期的通话索引
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

using Microsoft.Extensions.Logging;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    /// <summary>
    /// 简单 grain ,它允许设置两个参数然后乘以它们。
    /// </summary>
    public class AsyncSimpleGrain : SimpleGrain, ISimpleGrainWithAsyncMethods
    {
        private TaskCompletionSource<int> resolver = null!;

        public AsyncSimpleGrain(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public async Task<int> GetAxB_Async()
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            return await GetAxB();
        }

        public async Task<int> GetAxB_Async(int a, int b)
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            return await GetAxB(a, b);
        }
        public async Task SetA_Async(int a)
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            await SetA(a);
        }
        public async Task SetB_Async(int b)
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            await SetB(b);
        }

        public async Task IncrementA_Async()
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            await IncrementA();
        }

        public async Task<int> GetA_Async()
        {
            await Task.Delay(1000); // 仅延迟履行为测试目的的许诺
            return await GetA();
        }

        public Task<int> GetX()
        {
            resolver = new TaskCompletionSource<int>();
            return resolver.Task;
        }

        public Task SetX(int x)
        {
            resolver.SetResult(x);
            return Task.CompletedTask;
        }
    }
}

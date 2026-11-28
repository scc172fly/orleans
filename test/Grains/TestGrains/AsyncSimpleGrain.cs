using Microsoft.Extensions.Logging;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    /// <summary>
    /// 一个简单的 Grain，允许设置两个参数，然后将它们相乘。
    /// </summary>
    public class AsyncSimpleGrain : SimpleGrain, ISimpleGrainWithAsyncMethods
    {
        private TaskCompletionSource<int> resolver = null!;

        public AsyncSimpleGrain(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        public async Task<int> GetAxB_Async()
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
            return await GetAxB();
        }

        public async Task<int> GetAxB_Async(int a, int b)
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
            return await GetAxB(a, b);
        }
        public async Task SetA_Async(int a)
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
            await SetA(a);
        }
        public async Task SetB_Async(int b)
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
            await SetB(b);
        }

        public async Task IncrementA_Async()
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
            await IncrementA();
        }

        public async Task<int> GetA_Async()
        {
            await Task.Delay(1000); // 仅为测试目的延迟 Promise 的完成
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

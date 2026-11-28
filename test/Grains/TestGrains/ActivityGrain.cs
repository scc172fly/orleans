using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class ActivityGrain : IActivityGrain
    {
        public Task<ActivityData> GetActivityId()
        {
            var activity = Activity.Current;
            if (activity == null)
            {
                return Task.FromResult(default(ActivityData)!);
            }

            var result = new ActivityData()
            {
                Id = activity.Id,
                TraceState = activity.TraceStateString,
                Baggage = activity.Baggage.ToList()!,
            };

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 用于测试 IAsyncEnumerable 活动跟踪的 Grain 实现。
    /// </summary>
    public class AsyncEnumerableActivityGrain : Grain, IAsyncEnumerableActivityGrain
    {
        public async IAsyncEnumerable<ActivityData> GetActivityDataStream(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var activity = Activity.Current;
                var data = activity is null
                    ? new ActivityData()
                    : new ActivityData
                    {
                        Id = activity.Id,
                        TraceState = activity.TraceStateString,
                        Baggage = activity.Baggage.ToList()!,
                    };

                yield return data;

                // 短暂延迟，以便活动上下文正确传播
                await Task.Yield();
            }
        }
    }
}

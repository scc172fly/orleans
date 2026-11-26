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
    /// Grain 测试应用 IAsyncEnumerable 活动追踪。
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

                // 进行小规模拖延,以便适当开展宣传活动
                await Task.Yield();
            }
        }
    }
}

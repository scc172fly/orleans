using System.Reflection;
using Orleans.Durable.Tasks;
using Xunit;

namespace Orleans.Durable.Tasks.Abstractions.Tests;

/// <summary>
/// 固定宿主持久化的抽象类型的传输格式名称（例如日志中记录的响应），确保
/// 类型重命名后仍能读取已有记录。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
[TestCategory("BVT")]
public class PersistedTypeAliasTests
{
    public static TheoryData<Type, string> PersistedTypes() => new()
    {
        { typeof(TaskId), "Orleans.Durable.Tasks.TaskId" },
        { typeof(DurableTask), "Orleans.Durable.Tasks.DurableTask" },
        { typeof(DurableTask<>), "Orleans.Durable.Tasks.DurableTask`1" },
        { typeof(DurableTaskStatus), "Orleans.Durable.Tasks.DurableTaskStatus" },
        { typeof(DurableTaskResponseKind), "Orleans.Durable.Tasks.DurableTaskResponseKind" },
        { typeof(DurableTaskResponse), "Orleans.Durable.Tasks.DurableTaskResponse" },
        { typeof(SuccessDurableTaskResponse), "Orleans.Durable.Tasks.SuccessDurableTaskResponse" },
        { typeof(DurableTaskResponse<>), "Orleans.Durable.Tasks.DurableTaskResponse`1" },
        { typeof(ExceptionDurableTaskResponse), "Orleans.Durable.Tasks.ExceptionDurableTaskResponse" },
        { typeof(CanceledDurableTaskResponse), "Orleans.Durable.Tasks.CanceledDurableTaskResponse" },
        { typeof(PendingDurableTaskResponse), "Orleans.Durable.Tasks.PendingDurableTaskResponse" },
        { typeof(SubscribedDurableTaskResponse), "Orleans.Durable.Tasks.SubscribedDurableTaskResponse" },
        { typeof(DurableTaskStartOptions), "Orleans.Durable.Tasks.DurableTaskStartOptions" },
        { typeof(PollingOptions), "Orleans.Durable.Tasks.PollingOptions" },
        { typeof(DurableTaskAttemptCancellationReason), "Orleans.Durable.Tasks.DurableTaskAttemptCancellationReason" },
        { typeof(DurableTaskAbortedException), "Orleans.Durable.Tasks.DurableTaskAbortedException" },
        { typeof(DurableTaskAttemptCanceledException), "Orleans.Durable.Tasks.DurableTaskAttemptCanceledException" },
        { typeof(DurableTaskVersionException), "Orleans.Durable.Tasks.DurableTaskVersionException" },
    };

    [Theory]
    [MemberData(nameof(PersistedTypes))]
    public void PersistedTypesKeepTheirAliases(Type type, string alias)
        => Assert.Equal(alias, Assert.Single(type.GetCustomAttributes<AliasAttribute>(inherit: false)).Alias);

    [Fact]
    public void EveryAliasedTypeIsPinned()
    {
        var pinned = PersistedTypes().Select(static row => row.Data.Item1).ToHashSet();
        var aliased = typeof(DurableTask).Assembly.GetTypes().Where(static type => type.IsDefined(typeof(AliasAttribute), inherit: false));

        Assert.All(aliased, type => Assert.Contains(type, pinned));
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Scheduler;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 在 Grain 激活实例的调度器上运行测试代码，与 Silo 运行持久化任务运行时的方式相同。
/// </summary>
/// <remarks>
/// 运行时为每个执行实例捕获当前调度器，并依赖其激活实例每次只运行一个工作项的保证。
/// 在激活实例的 <c>ActivationTaskScheduler</c> 上运行测试即可向运行时提供这一保证，因此
/// 测试、运行时和各执行实例仅在等待操作处交错执行。
/// </remarks>
internal static class ActivationScheduler
{
    // 测试在其激活实例上遗留的排队工作（例如后台写入）可能在测试完成后运行，
    // 因此激活实例的服务在测试进程的整个生命周期内保持有效。
    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddMetrics()
        .AddSingleton<OrleansInstruments>()
        .AddSingleton<SchedulerInstruments>()
        .BuildServiceProvider();

    public static Task RunAsync(Func<Task> body)
    {
        var activation = Substitute.For<IGrainContext>();
        activation.ActivationServices.Returns(Services);
        var workItems = new WorkItemGroup(
            activation,
            Options.Create(new SchedulingOptions()),
            Services.GetRequiredService<SchedulerInstruments>());
        activation.Scheduler.Returns(workItems);

        return Task.Factory.StartNew(
            body,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            workItems.TaskScheduler).Unwrap();
    }
}

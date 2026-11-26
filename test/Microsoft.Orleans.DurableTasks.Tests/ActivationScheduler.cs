using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Scheduler;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 运行 grain 激活调度器上的测试代码,因为 silo 运行耐久任务运行时间。
/// </summary>
/// <remarks>
/// 运行时为每次执行捕捉当前调度器, 并依靠它启动一个工作项
/// 一次进行测试 <c>ActivationTaskScheduler</c> 给运行时间保证,所以
/// 测试,运行时间,每次执行 只在等待时中断。
/// </remarks>
internal static class ActivationScheduler
{
    // 测试完成后,在启动时排队的试验(例如背景文字)可以运行的工作,如背景文字,
    // 启动器的服务为测试过程直播
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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Runtime;

namespace Orleans.Hosting
{
    /// <summary>
    /// 下 silo 建设启动扩展 。
    /// </summary>
    public static class SiloBuilderStartupExtensions
    {
        /// <summary>
        /// 添加启动任务,在 silo 启动时执行。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="stage">
        /// 执行启动任务的阶段,请参见 <see cref="ServiceLifecycleStage"/>中的值。
        /// </param>
        /// <typeparam name="TStartup">
        /// 启动任务类型,包括用于依赖注射的公共建筑师。
        /// </typeparam>
        /// <returns>
        /// 提供 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder AddStartupTask<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStartup>(
            this ISiloBuilder builder,
            int stage = ServiceLifecycleStage.Active)
            where TStartup : class, IStartupTask
        {
            return builder.AddStartupTask((sp, ct) => ActivatorUtilities.GetServiceOrCreateInstance<TStartup>(sp).Execute(ct), stage);
        }

        /// <summary>
        /// 添加启动任务,在 silo 启动时执行。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="startupTask">
        /// 启动任务
        /// </param>
        /// <param name="stage">
        /// 执行启动任务的阶段,请参见 <see cref="ServiceLifecycleStage"/>中的值。
        /// </param>
        /// <returns>
        /// 提供 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder AddStartupTask(
            this ISiloBuilder builder,
            IStartupTask startupTask,
            int stage = ServiceLifecycleStage.Active)
        {
            return builder.AddStartupTask((sp, ct) => startupTask.Execute(ct), stage);
        }

        /// <summary>
        /// 添加启动任务,在 silo 启动时执行。
        /// </summary>
        /// <param name="builder">
        /// 建筑工
        /// </param>
        /// <param name="startupTask">
        /// 启动任务
        /// </param>
        /// <param name="stage">
        /// 执行启动任务的阶段,请参见 <see cref="ServiceLifecycleStage"/>中的值。
        /// </param>
        /// <returns>
        /// 提供 <see cref="ISiloBuilder"/>。
        /// </returns>
        public static ISiloBuilder AddStartupTask(
            this ISiloBuilder builder,
            Func<IServiceProvider, CancellationToken, Task> startupTask,
            int stage = ServiceLifecycleStage.Active)
        {
            builder.ConfigureServices(services =>
                services.AddTransient<ILifecycleParticipant<ISiloLifecycle>>(sp =>
                    new StartupTask(
                        sp,
                        startupTask,
                        stage)));
            return builder;
        }

        /// <inheritdoc />
        private class StartupTask : ILifecycleParticipant<ISiloLifecycle>
        {
            private readonly IServiceProvider serviceProvider;
            private readonly Func<IServiceProvider, CancellationToken, Task> startupTask;

            private readonly int stage;

            public StartupTask(
                IServiceProvider serviceProvider,
                Func<IServiceProvider, CancellationToken, Task> startupTask,
                int stage)
            {
                this.serviceProvider = serviceProvider;
                this.startupTask = startupTask;
                this.stage = stage;
            }

            /// <inheritdoc />
            public void Participate(ISiloLifecycle lifecycle)
            {
                lifecycle.Subscribe<StartupTask>(
                    this.stage,
                    cancellation => this.startupTask(this.serviceProvider, cancellation));
            }
        }
    }
}

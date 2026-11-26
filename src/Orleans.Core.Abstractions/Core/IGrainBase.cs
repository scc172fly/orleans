using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Orleans.Timers;

namespace Orleans
{
    /// <summary>
    /// grain 执行的接口
    /// </summary>
    public interface IGrainBase
    {
        /// <summary>
        /// 获取 grain 上下文。
        /// </summary>
        IGrainContext GrainContext { get; }

        /// <summary>
        /// 处理激活的 grain 执行方法取代的方法。
        /// </summary>
        /// <param name="token">取消牌用于表示激活应迅速中止。</param>
        /// <returns>A <see cref="Task"/> 表示操作。</returns>
        Task OnActivateAsync(CancellationToken token) => Task.CompletedTask;

        /// <summary>
        /// 处理停用时的 grain 执行方法取代的方法。
        /// </summary>
        /// <param name="reason">停止使用的理由。</param>
        /// <param name="token">取消牌用于表示应当迅速完成解除功能的标记。</param>
        /// <returns>A <see cref="Task"/> 表示操作。</returns>
        Task OnDeactivateAsync(DeactivationReason reason, CancellationToken token) => Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="IGrainBase"/> 执行的辅助方法。
    /// </summary>
    public static class GrainBaseExtensions
    {
        /// <summary>
        /// 禁用此功能 grain 当前时值后激活 grain 方法调用已完成。
        /// 此调用将标志着当前 grain 的激活, 在当前方法结束时停止使用并移除 。
        /// 下次调用此 grain 将导致使用不同的激活, 典型地意味着在运行时将自动生成新的激活。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 是 <see langword="null"/>.</exception>
        public static void DeactivateOnIdle(this IGrainBase grain)
        {
            ArgumentNullException.ThrowIfNull(grain);
            grain.GrainContext.Deactivate(new(DeactivationReasonCode.ApplicationRequested, $"{nameof(DeactivateOnIdle)} was called."));
        }

        /// <summary>
        /// 开始尝试将这个实例移到另一个地方。
        /// 移民捕捉到当前的 <see cref="RequestContext"/>,向启动的安置主任提供,以便其在选择新地点时能够加以考虑。
        /// 移民将不同步地发生,而没有执行任何请求;如果启动的安置主任不选择其他地点,则不会出现移徙。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 是 <see langword="null"/>.</exception>
        public static void MigrateOnIdle(this IGrainBase grain)
        {
            ArgumentNullException.ThrowIfNull(grain);
            grain.GrainContext.Migrate(RequestContext.CallContextData?.Value.Values);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync(this IGrainBase grain, Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task RunRequestAsync(this IGrainBase grain, Func<CancellationToken, Task> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Func<CancellationToken, Task>, object?>(
                grain.GrainContext,
                static (callback, cancellationToken) => callback(cancellationToken),
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<TState, object?>(grain.GrainContext, callback, state, options, cancellationToken);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync(this IGrainBase grain, Action<CancellationToken> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task RunRequestAsync(this IGrainBase grain, Action<CancellationToken> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Action<CancellationToken>, object?>(
                grain.GrainContext,
                static (callback, cancellationToken) =>
                {
                    callback(cancellationToken);
                    return Task.CompletedTask;
                },
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Action<TState, CancellationToken> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>A <see cref="Task"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task RunRequestAsync<TState>(this IGrainBase grain, Action<TState, CancellationToken> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<(Action<TState, CancellationToken> Callback, TState State), object?>(
                grain.GrainContext,
                static (state, cancellationToken) =>
                {
                    state.Callback(state.State, cancellationToken);
                    return Task.CompletedTask;
                },
                (callback, state),
                options,
                cancellationToken);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>A <see cref="Task{TResult}"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task<TResult> RunRequestAsync<TResult>(this IGrainBase grain, Func<CancellationToken, Task<TResult>> callback, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>A <see cref="Task{TResult}"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task<TResult> RunRequestAsync<TResult>(this IGrainBase grain, Func<CancellationToken, Task<TResult>> callback, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<Func<CancellationToken, Task<TResult>>, TResult>(
                grain.GrainContext,
                static (callback, cancellationToken) => callback(cancellationToken),
                callback,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>A <see cref="Task{TResult}"/> ,与回调结果完全相同。</returns>
        /// <remarks><inheritdoc cref="RunRequestAsync{TState, TResult}(IGrainBase, Func{TState, CancellationToken, Task{TResult}}, TState, LocalRequestOptions, CancellationToken)" path="/remarks/node()"/></remarks>
        public static Task<TResult> RunRequestAsync<TState, TResult>(this IGrainBase grain, Func<TState, CancellationToken, Task<TResult>> callback, TState state, CancellationToken cancellationToken = default) =>
            RunRequestAsync(grain, callback, state, default(LocalRequestOptions), cancellationToken);

        /// <summary>
        /// 运行 <paramref name="callback"/> 作为此 grain 激活的请求。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">回调要运行。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="options">请求选项。</param>
        /// <param name="cancellationToken">
        /// 取消在排队时的请求。 调回开始后, 将牌传给调回 。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <typeparam name="TResult">回调结果的类型。</typeparam>
        /// <returns>A <see cref="Task{TResult}"/> ,与回调结果完全相同。</returns>
        /// <remarks>
        /// <para>
        /// 请求与启动的其他请求一起排期。 默认情况下, 请求遵循 grain的再生规则 :
        /// 在一个非返回 grain上,回调完全运行,与 grain 方法呼叫相同。
        /// 请求通过 grain 调用过滤器,作为 <c>IGrainRequestInvoker.RunRequestAsync</c>。
        /// 它启动一个新的呼叫链 默认情况下,它能保持激活的活力
        /// 无效或设定结果的呼叫过滤器,不援引请求即可完成返回的任务并取得该结果。
        /// 回调状态和结果通过参考而不是复制传递。
        /// </para>
        /// <para>
        /// 使用此方法运行背景任务( 如流或短信处理器) 的代码, 作为 grain 请求 。
        /// 它可以被从任何线索调用。在一个未恢复的 grain上,等待在请求中返回的任务,要求启动同样的僵局,
        /// 等待着从回调中 发出同一信号
        /// </para>
        /// <para>
        /// 如果在请求开始之前启动的禁用装置,返回的任务故障
        /// <see cref="OrleansMessageRejectionException"/>.
        /// 提出调用过滤器和追踪不同界面的名称、方法名称或参数的请求,
        /// 队列自定义 <see cref="ILocalRequest"/> 使用 <see cref="IGrainContext.ReceiveLocalRequest"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static Task<TResult> RunRequestAsync<TState, TResult>(this IGrainBase grain, Func<TState, CancellationToken, Task<TResult>> callback, TState state, LocalRequestOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            return RunRequestCore<TState, TResult>(grain.GrainContext, callback, state, options, cancellationToken);
        }

        private static Task<TResult> RunRequestCore<TState, TResult>(
            IGrainContext grainContext,
            Func<TState, CancellationToken, Task> callback,
            TState state,
            LocalRequestOptions options,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<TResult>(cancellationToken);
            }

            // 启动程序一旦排队,即拥有请求权,并在请求后处置。
#pragma warning disable CA2000 // Dispose objects before losing scope
            var request = new GrainRequest<TState, TResult>(callback, state, cancellationToken);
#pragma warning restore CA2000 // Dispose objects before losing scope
            try
            {
                grainContext.ReceiveLocalRequest(request, options);
            }
            catch (Exception exception)
            {
                // 完成请求后,取消登记即予以解除,并报告在返回的任务中未能履行。
                ((ILocalRequest)request).Complete(Response.FromException(exception));
            }

            return request.Task;
        }

        /// <summary>
        /// 创建 grain 计时器。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="options">
        /// 创建计时器的选项。
        /// </param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        /// <returns>
        /// 代表计时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 计时器默认不保持 grains 活动。设置 <see cref="GrainTimerCreationOptions.KeepAlive"/>
        /// <see langword="true"/> 导致每个计时器的勾当延长 grain 激活的寿命。
        /// 如果定时器的计时器不常, grain 仍然可以因闲置而停止使用。
        /// 当一个 grain 被停用时,所有活动定时器将被丢弃。
        /// </para>
        /// <para>
        /// 在调回返回的 <see cref="Task"/> 解决之前,将不排期下一次计时器计数。
        /// 也就是说,一个时间推回永远不会同时执行。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="true"/>,则允许回调定时器
        /// 与另一配偶或 grain 方法调用和其他定时器。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="false"/>,则回调时将尊重
        /// grain的重新恢复设置,就像典型 grain 方法调用。
        /// </para>
        /// <para>
        /// 可随时调用 <see cref="IGrainTimer"/> <see cref="IDisposable.Dispose"/> 方法停止计时器。
        /// 使用定时器可防止任何进一步的定时器计数器时间点被排入日程。
        /// </para>
        /// <para>
        /// 定时器的到期时间和期限可以通过调用其 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法来更新。
        /// 每次更新计时器,将根据最新时间排定下一次计时器的时间安排。
        /// 以后的勾选将在更新期结束后安排。
        /// 请注意,该行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 将记录被回调的例外, 但不会阻止下次计时器滴记排队 。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="grain"/> 或 <paramref name="callback"/> 是 <see langword="null"/>。</exception>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(grain);
            if (grain is Grain grainClass)
            {
                grainClass.EnsureRuntime();
                return grainClass.Runtime.TimerRegistry.RegisterGrainTimer(grainClass.GrainContext, callback, state, options);
            }

            return grain.GrainContext.ActivationServices.GetRequiredService<ITimerRegistry>().RegisterGrainTimer(grain.GrainContext, callback, state, options);
        }

        /// <summary>
        /// 创建 grain 计时器。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="options">
        /// 创建计时器的选项。
        /// </param>
        /// <returns>
        /// 代表计时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 计时器默认不保持 grains 活动。设置 <see cref="GrainTimerCreationOptions.KeepAlive"/>
        /// <see langword="true"/> 导致每个计时器的勾当延长 grain 激活的寿命。
        /// 如果定时器的计时器不常, grain 仍然可以因闲置而停止使用。
        /// 当一个 grain 被停用时,所有活动定时器将被丢弃。
        /// </para>
        /// <para>
        /// 在调回返回的 <see cref="Task"/> 解决之前,将不排期下一次计时器计数。
        /// 也就是说,一个时间推回永远不会同时执行。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="true"/>,则允许回调定时器
        /// 与另一配偶或 grain 方法调用和其他定时器。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="false"/>,则回调时将尊重
        /// grain的重新恢复设置,就像典型 grain 方法调用。
        /// </para>
        /// <para>
        /// 可随时调用 <see cref="IGrainTimer"/> <see cref="IDisposable.Dispose"/> 方法停止计时器。
        /// 使用定时器可防止任何进一步的定时器计数器时间点被排入日程。
        /// </para>
        /// <para>
        /// 定时器的到期时间和期限可以通过调用其 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法来更新。
        /// 每次更新计时器,将根据最新时间排定下一次计时器的时间安排。
        /// 以后的勾选将在更新期结束后安排。
        /// 请注意,该行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 将记录被回调的例外, 但不会阻止下次计时器滴记排队 。
        /// </para>
        /// </remarks>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<CancellationToken, Task> callback, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (callback, cancellationToken) => callback(cancellationToken), callback, options);
        }

        /// <summary>
        /// 创建 grain 计时器。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="options">创建计时器的选项。</param>
        /// <returns>代表计时器的 <see cref="IGrainTimer"/> 实例。</returns>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<Task> callback, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (callback, cancellationToken) => callback(), callback, options);
        }

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, GrainTimerCreationOptions)"/>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="options">创建计时器的选项。</param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, Task> callback, TState state, GrainTimerCreationOptions options)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return RegisterGrainTimer(grain, static (state, _) => state.Callback(state.State), (Callback: callback, State: state), options);
        }

        /// <summary>
        /// 创建 grain 计时器。
        /// </summary>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="dueTime">
        /// <see cref="TimeSpan"/> 表示在使用 <see cref="IGrainTimer"/> 构建时指定的回调方法之前需要延迟的时间。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 以阻止计时器启动。
        /// 指定 <see cref="TimeSpan.Zero"/> 立即开始计时器。
        /// </param>
        /// <param name="period">
        /// 构建 <see cref="IGrainTimer"/> 时指定的回调方法的引用间隔时间。
        /// 指定用于禁用定期信号的 <see cref="Timeout.InfiniteTimeSpan "/> 。
        /// </param>
        /// <returns>
        /// 代表计时器的 <see cref="IGrainTimer"/> 实例。
        /// </returns>
        /// <remarks>
        /// <para>
        /// Grain 计时器默认不保持 grains 活动。设置 <see cref="GrainTimerCreationOptions.KeepAlive"/>
        /// <see langword="true"/> 导致每个计时器的勾当延长 grain 激活的寿命。
        /// 如果定时器的计时器不常, grain 仍然可以因闲置而停止使用。
        /// 当一个 grain 被停用时,所有活动定时器将被丢弃。
        /// </para>
        /// <para>
        /// 在调回返回的 <see cref="Task"/> 解决之前,将不排期下一次计时器计数。
        /// 也就是说,一个时间推回永远不会同时执行。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="true"/>,则允许回调定时器
        /// 与另一配偶或 grain 方法调用和其他定时器。
        /// 如果 <see cref="GrainTimerCreationOptions.Interleave"/> 设为 <see langword="false"/>,则回调时将尊重
        /// grain的重新恢复设置,就像典型 grain 方法调用。
        /// </para>
        /// <para>
        /// 可随时调用 <see cref="IGrainTimer"/> <see cref="IDisposable.Dispose"/> 方法停止计时器。
        /// 使用定时器可防止任何进一步的定时器计数器时间点被排入日程。
        /// </para>
        /// <para>
        /// 定时器的到期时间和期限可以通过调用其 <see cref="IGrainTimer.Change(TimeSpan, TimeSpan)"/> 方法来更新。
        /// 每次更新计时器,将根据最新时间排定下一次计时器的时间安排。
        /// 以后的勾选将在更新期结束后安排。
        /// 请注意,该行为与 <see cref="Timer.Change(TimeSpan, TimeSpan)"/> 方法相同。
        /// </para>
        /// <para>
        /// 将记录被回调的例外, 但不会阻止下次计时器滴记排队 。
        /// </para>
        /// </remarks>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<Task> callback, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        public static IGrainTimer RegisterGrainTimer(this IGrainBase grain, Func<CancellationToken, Task> callback, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="dueTime">调回前要延迟的时间。</param>
        /// <param name="period">回调引用之间的时间间隔。</param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, Task> callback, TState state, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, state, new() { DueTime = dueTime, Period = period });

        /// <inheritdoc cref="RegisterGrainTimer(IGrainBase, Func{Task}, TimeSpan, TimeSpan)"/>
        /// <param name="grain"> grain 实例。</param>
        /// <param name="callback">计时器回调, 定时器到期时将使用该调时器。</param>
        /// <param name="state">国家通过回调。</param>
        /// <param name="dueTime">调回前要延迟的时间。</param>
        /// <param name="period">回调引用之间的时间间隔。</param>
        /// <typeparam name="TState"> <paramref name="state"/> 参数的类型。</typeparam>
        public static IGrainTimer RegisterGrainTimer<TState>(this IGrainBase grain, Func<TState, CancellationToken, Task> callback, TState state, TimeSpan dueTime, TimeSpan period)
            => RegisterGrainTimer(grain, callback, state, new() { DueTime = dueTime, Period = period });
    }

    /// <summary>
    /// 禁用的信息解释代码。
    /// </summary>
    [GenerateSerializer]
    public enum DeactivationReasonCode : byte
    {
        /// <summary>
        /// 没有提供理由。
        /// </summary>
        None,

        /// <summary>
        /// 进程目前正在关闭。
        /// </summary>
        ShuttingDown,

        /// <summary>
        /// 启动 grain 失败 。
        /// </summary>
        ActivationFailed,

        /// <summary>
        /// 发行的 grain 目录内部故障影响这种激活。
        /// </summary>
        /// <remarks>
        /// 这可能是由于例如,一个程序在主机启动此 grain 目录分区时失败所致。
        /// </remarks>
        DirectoryFailure,

        /// <summary>
        /// 启动已闲置 。
        /// </summary>
        ActivationIdle,

        /// <summary>
        /// 启动对命令或请求没有反应
        /// </summary>
        ActivationUnresponsive,

        /// <summary>
        /// 另一例 grain 已经启动。
        /// </summary>
        DuplicateActivation,

        /// <summary>
        /// 启动程序收到一个请求,但无法由本地运行程序处理。
        /// </summary>
        IncompatibleRequest,

        /// <summary>
        /// 发生了应用程序错误。
        /// </summary>
        ApplicationError,

        /// <summary>
        /// 请求启动此激活的应用程序 。
        /// </summary>
        ApplicationRequested,

        /// <summary>
        /// 启动程序正在迁移到一个新的地点
        /// </summary>
        Migrating,

        /// <summary>
        /// 激活启动的运行时间 。
        /// </summary>
        RuntimeRequested,

        /// <summary>
        /// 运行时间检测到应用程序的内存率很低,并强行决定停止使用。
        /// </summary>
        HighMemoryPressure,
    }

}

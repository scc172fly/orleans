using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 提供观察生命周期的功能。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>仅供单次使用，不支持多个启动/停止周期。</description></item>
    /// <item><description>启动后，不能再订阅其他观察者。</description></item>
    /// <item><description>OnStart 按顺序启动各阶段，直至首次失败或取消。</description></item>
    /// <item><description>OnStop 从已启动的最高阶段开始，按相反顺序停止各阶段。</description></item>
    /// <item><description>OnStop 即使被取消，也不受错误影响，仍停止所有阶段。</description></item>
    /// </list>
    /// </remarks>
    public abstract partial class LifecycleSubject : ILifecycleSubject
    {
        private readonly List<OrderedObserver> subscribers = [];

        /// <summary>
        /// 获取用于报告生命周期活动的日志记录器。
        /// </summary>
        protected readonly ILogger Logger;
        private int? _highStage = null;

        /// <summary>
        /// 初始化 <see cref="LifecycleSubject"/> 类的新实例。
        /// </summary>
        /// <param name="logger">用于报告生命周期活动的日志记录器。</param>
        protected LifecycleSubject(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            Logger = logger;
        }

        /// <summary>
        /// 获取指定阶段编号对应的名称。
        /// </summary>
        /// <param name="stage">阶段编号。</param>
        /// <returns>阶段名称。</returns>
        protected virtual string GetStageName(int stage) => stage.ToString();

        /// <summary>
        /// 获取所有阶段编号及对应名称的集合。
        /// </summary>
        /// <seealso cref="ServiceLifecycleStage"/>
        /// <param name="type">生命周期阶段类。</param>
        /// <returns>所有阶段编号及对应名称的集合。</returns>
        protected static ImmutableDictionary<int, string> GetStageNames(Type type)
        {
            try
            {
                var result = ImmutableDictionary.CreateBuilder<int, string>();
                var fields = type.GetFields(
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (typeof(int).IsAssignableFrom(field.FieldType))
                    {
                        try
                        {
                            var value = (int)field.GetValue(null)!;
                            result[value] = $"{field.Name} ({value})";
                        }
                        catch
                        {
                            // 忽略。
                        }
                    }
                }

                return result.ToImmutable();
            }
            catch
            {
                return ImmutableDictionary<int, string>.Empty;
            }
        }

        /// <summary>
        /// 记录 <see cref="OnStart"/> 调用的实际观测性能。
        /// </summary>
        /// <param name="stage">阶段。</param>
        /// <param name="elapsed">从启动 <see cref="OnStart"/> 到其完成所经过的时间。</param>
        protected virtual void PerfMeasureOnStart(int stage, TimeSpan elapsed)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                var stageName = GetStageName(stage);
                LogLifecycleStageStarted(Logger, stageName, elapsed);
            }
        }

        /// <inheritdoc />
        public virtual async Task OnStart(CancellationToken cancellationToken = default)
        {
            if (this._highStage.HasValue) throw new InvalidOperationException("Lifecycle has already been started.");
            try
            {
                foreach (IGrouping<int, OrderedObserver> observerGroup in this.subscribers
                    .GroupBy(orderedObserver => orderedObserver.Stage)
                    .OrderBy(group => group.Key))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OrleansLifecycleCanceledException($"Lifecycle start canceled at stage '{GetStageName(observerGroup.Key)}' by request.");
                    }

                    var stage = observerGroup.Key;
                    this._highStage = stage;
                    var stopWatch = ValueStopwatch.StartNew();
                    await Task.WhenAll(observerGroup.Select(orderedObserver => CallOnStart(orderedObserver, cancellationToken)));
                    stopWatch.Stop();
                    this.PerfMeasureOnStart(stage, stopWatch.Elapsed);

                    this.OnStartStageCompleted(stage);
                }
            }
            catch (Exception ex) when (ex is not OrleansLifecycleCanceledException)
            {
                LogErrorLifecycleStartFailure(Logger, ex, _highStage is { } highStage ? GetStageName(highStage) : "Unknown");
                throw;
            }

            static Task CallOnStart(OrderedObserver observer, CancellationToken cancellationToken)
            {
                try
                {
                    return observer.Observer?.OnStart(cancellationToken) ?? Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }
        }

        /// <summary>
        /// 表示 <see cref="OnStart"/> 已完成。
        /// </summary>
        /// <param name="stage">已完成的阶段。</param>
        protected virtual void OnStartStageCompleted(int stage) { }

        /// <summary>
        /// 记录 <see cref="OnStop"/> 调用的实际观测性能。
        /// </summary>
        /// <param name="stage">阶段。</param>
        /// <param name="elapsed">从启动 <see cref="OnStop"/> 到其完成所经过的时间。</param>
        protected virtual void PerfMeasureOnStop(int stage, TimeSpan elapsed)
        {
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                var stageName = GetStageName(stage);
                LogLifecycleStageStopped(Logger, stageName, elapsed);
            }
        }

        /// <inheritdoc />
        public virtual async Task OnStop(CancellationToken cancellationToken = default)
        {
            // 如果尚未启动，则不执行任何操作。
            if (!this._highStage.HasValue) return;
            var loggedCancellation = false;
            foreach (IGrouping<int, OrderedObserver> observerGroup in this.subscribers
                // 包括截至已启动的最高阶段在内的所有阶段。
                .Where(orderedObserver => orderedObserver.Stage <= _highStage && orderedObserver.Observer != null)
                .GroupBy(orderedObserver => orderedObserver.Stage)
                .OrderByDescending(group => group.Key))
            {
                if (cancellationToken.IsCancellationRequested && !loggedCancellation)
                {
                    LogWarningLifecycleStopCanceled(Logger, GetStageName(observerGroup.Key));
                    loggedCancellation = true;
                }

                var stage = observerGroup.Key;
                this._highStage = stage;
                try
                {
                    var stopwatch = ValueStopwatch.StartNew();
                    await Task.WhenAll(observerGroup.Select(orderedObserver => orderedObserver?.Observer is not null ? CallObserverStopAsync(orderedObserver.Observer, cancellationToken) : Task.CompletedTask));
                    stopwatch.Stop();
                    this.PerfMeasureOnStop(stage, stopwatch.Elapsed);
                }
                catch (Exception ex)
                {
                    LogWarningLifecycleStopFailure(Logger, ex, _highStage is { } highStage ? GetStageName(highStage) : "Unknown");
                }

                this.OnStopStageCompleted(stage);
            }
        }

        /// <summary>
        /// 调用生命周期观察者的停止回调。
        /// </summary>
        /// <param name="observer">要停止的观察者。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示观察者停止操作的任务。</returns>
        protected virtual Task CallObserverStopAsync(ILifecycleObserver observer, CancellationToken cancellationToken)
        {
            try
            {
                return observer.OnStop(cancellationToken) ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <summary>
        /// 表示 <see cref="OnStop"/> 已完成。
        /// </summary>
        /// <param name="stage">已完成的阶段。</param>
        protected virtual void OnStopStageCompleted(int stage) { }

        /// <inheritdoc />
        public virtual IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (this._highStage.HasValue) throw new InvalidOperationException("Lifecycle has already been started.");

            var orderedObserver = new OrderedObserver(stage, observer);
            this.subscribers.Add(orderedObserver);
            return orderedObserver;
        }

        /// <summary>
        /// 表示 <see cref="ILifecycleObservable"/> 在指定生命周期阶段中的参与情况。
        /// </summary>
        private class OrderedObserver : IDisposable
        {
            /// <summary>
            /// 获取观察者。
            /// </summary>
            public ILifecycleObserver? Observer { get; private set; }

            /// <summary>
            /// 获取观察者正在参与的阶段。
            /// </summary>
            public int Stage { get; }

            /// <summary>
            /// 初始化 <see cref="OrderedObserver"/> 类的新实例。
            /// </summary>
            /// <param name="stage">观察者正在参与的阶段。</param>
            /// <param name="observer">参与的观察者。</param>
            public OrderedObserver(int stage, ILifecycleObserver observer)
            {
                this.Stage = stage;
                this.Observer = observer;
            }

            /// <inheritdoc />
            public void Dispose() => Observer = null;
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.LifecycleStartFailure,
            Level = LogLevel.Error,
            Message = "Lifecycle start canceled due to errors at stage '{Stage}'."
        )]
        private static partial void LogErrorLifecycleStartFailure(ILogger logger, Exception ex, string stage);

        [LoggerMessage(
            EventId = (int)ErrorCode.LifecycleStopFailure,
            Level = LogLevel.Warning,
            Message = "Stopping lifecycle encountered an error at stage '{Stage}'. Continuing to stop."
        )]
        private static partial void LogWarningLifecycleStopFailure(ILogger logger, Exception ex, string stage);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Lifecycle stop operations canceled at stage '{Stage}' by request."
        )]
        private static partial void LogWarningLifecycleStopCanceled(ILogger logger, string stage);

        [LoggerMessage(
            EventId = (int)ErrorCode.SiloStartPerfMeasure,
            Level = LogLevel.Trace,
            SkipEnabledCheck = true,
            Message = "Starting lifecycle stage '{Stage}' took '{Elapsed}'."
        )]
        private static partial void LogLifecycleStageStarted(ILogger logger, string stage, TimeSpan elapsed);

        [LoggerMessage(
            EventId = (int)ErrorCode.SiloStartPerfMeasure,
            Level = LogLevel.Trace,
            SkipEnabledCheck = true,
            Message = "Stopping lifecycle stage '{Stage}' took '{Elapsed}'."
        )]
        private static partial void LogLifecycleStageStopped(ILogger logger, string stage, TimeSpan elapsed);
    }
}

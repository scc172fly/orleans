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
    /// <item><description>单用,不支持多个启动/停止周期。</description></item>
    /// <item><description>一旦开始,就没有其他观察员可以订阅。</description></item>
    /// <item><description>OnStart 开始阶段,直至第一次失败或取消为止。</description></item>
    /// <item><description>OnStop 以相反顺序停止状态 从最高开始阶段开始</description></item>
    /// <item><description>OnStop 停止所有阶段,即使错误被取消也不管其出错。</description></item>
    /// </list>
    /// </remarks>
    public abstract partial class LifecycleSubject : ILifecycleSubject
    {
        private readonly List<OrderedObserver> subscribers = [];

        /// <summary>
        /// 获取用于报告生命周期活动的日志。
        /// </summary>
        protected readonly ILogger Logger;
        private int? _highStage = null;

        /// <summary>
        /// 初始化 <see cref="LifecycleSubject"/> 类的新实例。
        /// </summary>
        /// <param name="logger">用于报告生命周期活动的日志员</param>
        protected LifecycleSubject(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            Logger = logger;
        }

        /// <summary>
        /// 获取指定的数字阶段的名称。
        /// </summary>
        /// <param name="stage">阶段编号。</param>
        /// <returns>阶段的名称。</returns>
        protected virtual string GetStageName(int stage) => stage.ToString();

        /// <summary>
        /// 收集所有舞台编号及其相应名称。
        /// </summary>
        /// <seealso cref="ServiceLifecycleStage"/>
        /// <param name="type">生命周期阶段类。</param>
        /// <returns>收集所有阶段编号及其相应名称。</returns>
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
                            // 忽略。 忽略。
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
        /// 记录 <see cref="OnStart"/> 呼叫的观察性能。
        /// </summary>
        /// <param name="stage">舞台,舞台,舞台</param>
        /// <param name="elapsed">之前的时期 <see cref="OnStart"/> 程序启动后即完成。</param>
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
        /// 表示已完成 <see cref="OnStart"/> 。
        /// </summary>
        /// <param name="stage">完成的阶段。</param>
        protected virtual void OnStartStageCompleted(int stage) { }

        /// <summary>
        /// 记录 <see cref="OnStop"/> 呼叫的观察性能。
        /// </summary>
        /// <param name="stage">舞台,舞台,舞台</param>
        /// <param name="elapsed">之前的时期 <see cref="OnStop"/> 程序启动后即完成。</param>
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
            // 如果尚未开始, 则不做任何
            if (!this._highStage.HasValue) return;
            var loggedCancellation = false;
            foreach (IGrouping<int, OrderedObserver> observerGroup in this.subscribers
                // 包括最高开始阶段
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
        /// 启动停止呼叫 以观察生命周期
        /// </summary>
        /// <param name="observer">观察员停下来。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>代表观察员停止行动的任务。</returns>
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
        /// 表示已完成 <see cref="OnStop"/> 。
        /// </summary>
        /// <param name="stage">完成的阶段。</param>
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
        /// 表示 <see cref="ILifecycleObservable"/>在特定生命周期阶段的参与。
        /// </summary>
        private class OrderedObserver : IDisposable
        {
            /// <summary>
            /// 抓住观察者
            /// </summary>
            public ILifecycleObserver? Observer { get; private set; }

            /// <summary>
            /// 获得观察者参与的舞台。
            /// </summary>
            public int Stage { get; }

            /// <summary>
            /// 初始化 <see cref="OrderedObserver"/> 类的新实例。
            /// </summary>
            /// <param name="stage">观察员正在参与的阶段。</param>
            /// <param name="observer">与会观察员。</param>
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

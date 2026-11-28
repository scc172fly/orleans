using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Scheduler;
using Orleans.Serialization.Invocation;
using Orleans.Timers;

namespace Orleans.Runtime
{
    /// <summary>
    /// 各类系统服务的基类，例如 Grain 目录、提醒器服务等。
    /// 声明为 public，以供 GrainService 继承。
    /// 在通过重构消除继承关系之后，可改为 internal。
    /// </summary>
    public abstract partial class SystemTarget : ISystemTarget, ISystemTargetBase, IGrainContext, IGrainExtensionBinder, ISpanFormattable, IDisposable, IGrainTimerRegistry, IGrainCallCancellationExtension
    {
        private readonly Func<object?, Task> _handleRequest;
        private readonly SystemTargetGrainId _id;
        private readonly SystemTargetShared _shared;
        private readonly HashSet<IGrainTimer> _timers = [];
        private readonly Dictionary<Message, Task> _runningRequests = [];
        private readonly Dictionary<Type, object> _components = [];


        /// <summary>系统目标的 Silo 地址。</summary>
        public SiloAddress Silo => _shared.SiloAddress;
        internal GrainAddress ActivationAddress { get; }

        internal ActivationId ActivationId { get; set; }
        private readonly ILogger _logger;

        internal ILogger SchedulerLogger => _shared.SchedulerLogger;

        internal InsideRuntimeClient RuntimeClient => _shared.RuntimeClient;

        /// <inheritdoc/>
        public GrainReference GrainReference { get => field ??= _shared.GrainReferenceActivator.CreateReference(_id.GrainId, default); private set; }

        /// <inheritdoc/>
        public GrainId GrainId => _id.GrainId;

        /// <inheritdoc/>
        object IGrainContext.GrainInstance => this;

        /// <inheritdoc/>
        ActivationId IGrainContext.ActivationId => ActivationId;

        /// <inheritdoc/>
        GrainAddress IGrainContext.Address => ActivationAddress;

        private RuntimeMessagingTrace MessagingTrace => _shared.MessagingTrace;

        /// <summary>
        /// <see cref="SystemTarget"/> 已过时的无参数构造函数。
        /// <para>
        /// <b>破坏性变更：</b> 此构造函数已过时，调用时将抛出 <see cref="NotSupportedException"/>。
        /// 它仅用于满足特定的基于反射的场景，不应在应用程序代码中使用。
        /// </para>
        /// <para>
        /// 如果使用要求无参数构造函数的反射或序列化框架，
        /// 请更新代码，以使用带参数的构造函数：<see cref="SystemTarget(SystemTargetGrainId, SystemTargetShared)"/>。
        /// </para>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Do not call the empty constructor.")]
        protected SystemTarget() => throw new NotSupportedException();

        internal SystemTarget(GrainType grainType, SystemTargetShared shared)
            : this(SystemTargetGrainId.Create(grainType, shared.SiloAddress), shared)
        {
        }

        internal SystemTarget(SystemTargetGrainId grainId, SystemTargetShared shared)
        {
            _id = grainId;
            _shared = shared;
            _handleRequest = state => this.HandleNewRequest((Message)state!);
            ActivationId = ActivationId.GetDeterministic(grainId.GrainId);
            ActivationAddress = GrainAddress.GetAddress(Silo, _id.GrainId, ActivationId);
            _logger = shared.LoggerFactory.CreateLogger(GetType());
            WorkItemGroup = _shared.CreateWorkItemGroup(this);
            if (!Constants.IsSingletonSystemTarget(GrainId.Type))
            {
                _shared.GrainInstruments.IncrementSystemTargetCounts(Constants.SystemTargetName(GrainId.Type));
            }
        }

        internal WorkItemGroup WorkItemGroup { get; }

        /// <inheritdoc />
        public IServiceProvider ActivationServices => RuntimeClient.ServiceProvider;

        /// <inheritdoc />
        IGrainLifecycle IGrainContext.ObservableLifecycle => throw new NotImplementedException("IGrainContext.ObservableLifecycle is not implemented by SystemTarget");

        /// <inheritdoc />
        public IWorkItemScheduler Scheduler => WorkItemGroup;

        /// <summary>
        /// 获取指定类型的组件。
        /// </summary>
        /// <typeparam name="TComponent">组件类型。</typeparam>
        /// <returns>具有指定类型的组件。</returns>
        public TComponent? GetComponent<TComponent>()
        {
            TComponent? result;
            if (this is TComponent instanceResult)
            {
                result = instanceResult;
            }
            else if (_components.TryGetValue(typeof(TComponent), out var resultObj))
            {
                result = (TComponent)resultObj;
            }
            else if (typeof(TComponent) == typeof(PlacementStrategy))
            {
                result = (TComponent)(object)SystemTargetPlacementStrategy.Instance;
            }
            else if (typeof(TComponent) == typeof(ITimerRegistry))
            {
                result = (TComponent)(object)_shared.TimerRegistry;
            }
            else
            {
                result = default;
            }

            return result;
        }

        /// <inheritdoc />
        public void SetComponent<TComponent>(TComponent? instance) where TComponent : class
        {
            if (this is TComponent)
            {
                throw new ArgumentException("Cannot override a component which is implemented by this grain");
            }

            if (instance == null)
            {
                _components.Remove(typeof(TComponent));
                return;
            }

            _components[typeof(TComponent)] = instance;
        }

        internal async Task HandleNewRequest(Message request)
        {
            try
            {
                await RuntimeClient.Invoke(this, request);
            }
            catch
            {
                // 忽略。
            }
            finally
            {
                lock (_runningRequests)
                {
                    _runningRequests.Remove(request);
                }
            }
        }

        internal void HandleResponse(Message response)
        {
            RuntimeClient.ReceiveResponse(response);
        }

        /// <summary>
        /// 注册定时器，定期向此系统目标发送回调。
        /// </summary>
        /// <param name="callback">定时器回调，每当定时器到期时触发。</param>
        /// <param name="state">传递给回调的状态对象。</param>
        /// <param name="dueTime">
        /// 调用 <paramref name="callback"/> 前的延迟时长。
        /// 指定 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
        /// 指定 <see cref="TimeSpan.Zero"/> 可尽快调用回调。
        /// </param>
        /// <param name="period">
        /// <paramref name="callback"/> 的调用间隔。
        /// 指定 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 可禁用周期性触发。
        /// </param>
        /// <returns>
        /// 一个 <see cref="IDisposable"/> 对象，释放时将取消定时器。
        /// </returns>
        public IGrainTimer RegisterTimer(Func<object?, Task> callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = _shared.TimerRegistry
                .RegisterGrainTimer(this, static (state, _) => state.Callback(state.State), (Callback: callback, State: state), new() { DueTime = dueTime, Period = period, Interleave = true });
            return timer;
        }

        /// <summary>
        /// 注册定时器，定期向此系统目标发送回调。
        /// </summary>
        /// <param name="callback">定时器回调，每当定时器到期时触发。</param>
        /// <param name="dueTime">
        /// 调用 <paramref name="callback"/> 前的延迟时长。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
        /// 指定 <see cref="TimeSpan.Zero"/> 可尽快调用回调。
        /// </param>
        /// <param name="period">
        /// <paramref name="callback"/> 的调用间隔。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可禁用周期性触发。
        /// </param>
        /// <returns>
        /// 一个 <see cref="IDisposable"/> 对象，释放时将取消定时器。
        /// </returns>
        public IGrainTimer RegisterGrainTimer(Func<CancellationToken, Task> callback, TimeSpan dueTime, TimeSpan period)
        {
            CheckRuntimeContext();
            ArgumentNullException.ThrowIfNull(callback);
            var timer = _shared.TimerRegistry
                .RegisterGrainTimer(this, (state, ct) => state(ct), callback, new() { DueTime = dueTime, Period = period, Interleave = true });
            return timer;
        }

        /// <summary>
        /// 注册定时器，定期向此 Grain 发送回调。
        /// 此定时器会阻止当前 Grain 被停用。
        /// </summary>
        /// <param name="callback">定时器回调，每当定时器到期时触发。</param>
        /// <param name="state">传递给回调的状态对象。</param>
        /// <param name="dueTime">
        /// 调用 <paramref name="callback"/> 前的延迟时长。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可阻止定时器启动。
        /// 指定 <see cref="TimeSpan.Zero"/> 可尽快调用回调。
        /// </param>
        /// <param name="period">
        /// <paramref name="callback"/> 的调用间隔。
        /// 指定 <see cref="Timeout.InfiniteTimeSpan"/> 可禁用周期性触发。
        /// </param>
        /// <returns>
        /// 一个 <see cref="IDisposable"/> 对象，释放时将取消定时器。
        /// </returns>
        public IGrainTimer RegisterGrainTimer<TState>(Func<TState, CancellationToken, Task> callback, TState state, TimeSpan dueTime, TimeSpan period)
        {
            CheckRuntimeContext();
            ArgumentNullException.ThrowIfNull(callback);
            var timer = _shared.TimerRegistry
                .RegisterGrainTimer(this, callback, state, new() { DueTime = dueTime, Period = period, Interleave = true });
            return timer;
        }

        /// <inheritdoc/>
        public sealed override string ToString() => $"{this}";

        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

        bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => destination.TryWrite($"[SystemTarget: {Silo}/{_id}{ActivationId}]", out charsWritten);

        /// <summary>添加当前正在处理的消息的详细信息。</summary>
        internal string ToDetailedString()
        {
            lock (_runningRequests)
            {
                return $"{this} CurrentlyExecuting={_runningRequests}";
            }
        }

        /// <inheritdoc/>
        bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);

        /// <inheritdoc/>
        public (TExtension, TExtensionInterface) GetOrSetExtension<TExtension, TExtensionInterface>(Func<TExtension> newExtensionFunc)
            where TExtension : class, TExtensionInterface
            where TExtensionInterface : class, IGrainExtension
        {
            TExtension implementation;
            if (GetComponent<TExtensionInterface>() is object existing)
            {
                if (existing is TExtension typedResult)
                {
                    implementation = typedResult;
                }
                else
                {
                    throw new InvalidCastException($"Cannot cast existing extension of type {existing.GetType()} to target type {typeof(TExtension)}");
                }
            }
            else
            {
                implementation = newExtensionFunc();
                SetComponent<TExtensionInterface>(implementation);
            }

            var reference = GrainReference.Cast<TExtensionInterface>();
            return (implementation, reference);
        }

        /// <inheritdoc/>
        object? ITargetHolder.GetComponent(Type componentType)
        {
            object? result;
            if (componentType.IsAssignableFrom(GetType()))
            {
                result = this;
            }
            else if (_components.TryGetValue(componentType, out var resultObj))
            {
                result = resultObj;
            }
            else if (componentType == typeof(PlacementStrategy))
            {
                result = SystemTargetPlacementStrategy.Instance;
            }
            else if (typeof(IGrainExtension).IsAssignableFrom(componentType))
            {
                var implementation = ActivationServices.GetKeyedService<IGrainExtension>(componentType);
                if (implementation is null)
                {
                    throw new GrainExtensionNotInstalledException($"No extension of type {componentType} is installed on this instance and no implementations are registered for automated install");
                }

                _components[componentType] = implementation;
                result = implementation;
            }
            else
            {
                result = default;
            }

            return result;
        }

        /// <inheritdoc/>
        public TExtensionInterface GetExtension<TExtensionInterface>()
            where TExtensionInterface : class, IGrainExtension
        {
            if (GetComponent<TExtensionInterface>() is TExtensionInterface result)
            {
                return result;
            }

            var implementation = ActivationServices.GetKeyedService<IGrainExtension>(typeof(TExtensionInterface));
            if (!(implementation is TExtensionInterface typedResult))
            {
                throw new GrainExtensionNotInstalledException($"No extension of type {typeof(TExtensionInterface)} is installed on this instance and no implementations are registered for automated install");
            }

            SetComponent<TExtensionInterface>(typedResult);
            return typedResult;
        }

        /// <inheritdoc/>
        public void ReceiveMessage(object message)
        {
            var msg = (Message)message;
            switch (msg.Direction)
            {
                case Message.Directions.Request:
                case Message.Directions.OneWay:
                    {
                        MessagingTrace.OnEnqueueMessageOnActivation(msg, this);
                        using var suppressExecutionContext = new ExecutionContextSuppressor();
                        var task = new Task<Task>(_handleRequest, msg);
                        lock (_runningRequests)
                        {
                            _runningRequests[msg] = task;
                        }

                        task.Start(WorkItemGroup.TaskScheduler);
                        break;
                    }

                default:
                    LogInvalidMessage(_logger, msg);
                    msg.Dispose();
                    break;
            }
        }

        /// <inheritdoc/>
        void IGrainContext.ReceiveLocalRequest(ILocalRequest request, LocalRequestOptions options)
            => ReceiveMessage(RuntimeClient.CreateLocalRequest(this, request, options));

        /// <inheritdoc/>
        public object? GetTarget() => this;

        /// <inheritdoc/>
        public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) { }

        /// <inheritdoc/>
        public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken) { }

        /// <inheritdoc/>
        public Task Deactivated => Task.CompletedTask;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!Constants.IsSingletonSystemTarget(GrainId.Type))
            {
                _shared.GrainInstruments.DecrementSystemTargetCounts(Constants.SystemTargetName(GrainId.Type));
            }

            StopAllTimers();
        }

        /// <inheritdoc/>
        public void Rehydrate(IRehydrationContext context)
        {
            // 不支持迁移，但如果提供了上下文，则需要释放它。
            (context as IDisposable)?.Dispose();
        }

        /// <inheritdoc/>
        public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
        {
            // 不支持迁移。不执行任何操作：契约规定此方法会尝试迁移，但不保证迁移一定发生。
        }

        void IGrainTimerRegistry.OnTimerCreated(IGrainTimer timer) { lock (_timers) { _timers.Add(timer); } }
        void IGrainTimerRegistry.OnTimerDisposed(IGrainTimer timer) { lock (_timers) { _timers.Remove(timer); } }
        private void StopAllTimers()
        {
            List<IGrainTimer> timers;
            lock (_timers)
            {
                timers = [.. _timers];
                _timers.Clear();
            }

            foreach (var timer in timers)
            {
                timer.Dispose();
            }
        }

        internal void CheckRuntimeContext()
        {
            var context = RuntimeContext.Current;
            if (context is null)
            {
                ThrowMissingContext();
                void ThrowMissingContext() => throw new InvalidOperationException($"Access violation: attempted to access context '{this}' from null context.");
            }

            if (!ReferenceEquals(context, this))
            {
                ThrowAccessViolation(context);
                void ThrowAccessViolation(IGrainContext? currentContext) => throw new InvalidOperationException($"Access violation: attempt to access context '{this}' from different context, '{currentContext}'.");
            }
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.Runtime_Error_100097,
            Message = "Invalid message: {Message}"
        )]
        private static partial void LogInvalidMessage(ILogger logger, Message Message);

        ValueTask IGrainCallCancellationExtension.CancelRequestAsync(
            GrainId senderGrainId,
            CorrelationId messageId,
            CancellationToken cancellationToken)
            => this.RunOrQueueTask(
                static state => state.self.CancelRequestAsyncCore(state.senderGrainId, state.messageId, state.cancellationToken),
                (self: this, senderGrainId, messageId, cancellationToken));

        private ValueTask CancelRequestAsyncCore(
            GrainId senderGrainId,
            CorrelationId messageId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCancelRequest())
            {
                // 正在取消的消息可能尚未到达，因此重试数次。
                return RetryCancellationAfterDelay();
            }

            return ValueTask.CompletedTask;

            async ValueTask RetryCancellationAfterDelay()
            {
                var attemptsRemaining = 3;
                do
                {
                    await Task.Delay(1_000, cancellationToken);

                    if (TryCancelRequest())
                    {
                        return;
                    }
                } while (--attemptsRemaining > 0);
            }

            bool TryCancelRequest()
            {
                Message? message = null;

                lock (_runningRequests)
                {
                    // 检查正在运行的请求。
                    foreach (var runningRequest in _runningRequests.Keys)
                    {
                        if (runningRequest.Id == messageId && runningRequest.SendingGrain == senderGrainId)
                        {
                            message = runningRequest;
                            break;
                        }
                    }
                }

                var didCancel = false;
                if (message is not null)
                {
                    if (message.BodyObject is IInvokable invokableRequest)
                    {
                        didCancel = TryCancelInvokable(invokableRequest) || !invokableRequest.IsCancellable;
                    }
                    else
                    {
                        // 假定请求不可取消。
                        didCancel = true;
                    }
                }

                return didCancel;
            }

            bool TryCancelInvokable(IInvokable request)
            {
                try
                {
                    return request.TryCancel();
                }
                catch (Exception exception)
                {
                    LogErrorCancellationCallbackFailed(_logger, exception);
                    return true;
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "One or more cancellation callbacks failed."
        )]
        private static partial void LogErrorCancellationCallbackFailed(ILogger logger, Exception exception);
    }
}

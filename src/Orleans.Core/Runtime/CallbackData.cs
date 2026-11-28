using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    internal sealed partial class CallbackData
    {
        private const int StateNone = 0;
        private const int StateCompleted = 1;
        private const int StateCancellationRegistrationPending = 2;
        private const int StateCancellationRegistrationPublished = 4;
        private const int StateCancellationRequested = 8;

        private readonly SharedCallbackData shared;
        private readonly IResponseCompletionSource context;

        // 请求的调用对象描述其取消方式和响应超时。消息体可能在发送后发生变化：
        // 重试消息持有已序列化的消息体，消息体会在重试发送后释放。
        private readonly IInvokable? _invokable;
        private readonly long _startTimestamp;
        private int _state;
        private StatusResponse? lastKnownStatus;
        private CancellationTokenRegistration _cancellationTokenRegistration;

        public CallbackData(
            SharedCallbackData shared,
            IResponseCompletionSource ctx,
            Message msg)
        {
            this.shared = shared;
            this.context = ctx;
            this.Message = msg;
            _invokable = msg.BodyObject as IInvokable;
            _startTimestamp = shared.TimeProvider.GetTimestamp();
        }

        public Message Message { get; } // 可能包含响应管道使用的元数据。

        public bool IsCompleted => (Volatile.Read(ref _state) & StateCompleted) != 0;

        public void SubscribeForCancellation(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return;
            }

            if (!TryUpdateState(
                blockingFlags: StateCompleted | StateCancellationRegistrationPending | StateCancellationRegistrationPublished,
                clearFlags: StateNone,
                setFlags: StateCancellationRegistrationPending))
            {
                return;
            }

            var registration = cancellationToken.UnsafeRegister(static (arg, token) =>
            {
                var callbackData = (CallbackData)arg!;
                callbackData.OnCancellation(token);
            }, this);

            _cancellationTokenRegistration = registration;
            if (!TryUpdateState(
                blockingFlags: StateCompleted,
                clearFlags: StateCancellationRegistrationPending,
                setFlags: StateCancellationRegistrationPublished))
            {
                registration.Dispose();
            }
        }

        private bool TryUpdateState(int blockingFlags, int clearFlags, int setFlags)
        {
            var state = Volatile.Read(ref _state);
            while ((state & blockingFlags) == 0)
            {
                var observed = Interlocked.CompareExchange(ref _state, (state & ~clearFlags) | setFlags, state);
                if (observed == state)
                {
                    return true;
                }

                state = observed;
            }

            return false;
        }

        private void SignalCancellation()
        {
            // 仅取消遵循取消令牌的请求。
            // 并非所有目标都支持 IGrainCallCancellationExtension，因此在这些情况下发送取消消息可能导致错误。
            // 基础设施层本可以取消某些请求，但如果目标方法不支持取消，此处就不会利用这些机会。
            if (_invokable is { IsCancellable: true })
            {
                shared.CancellationManager?.SignalCancellation(Message.TargetSilo, Message.TargetGrain, Message.SendingGrain, Message.Id);
            }
        }

        public void OnStatusUpdate(StatusResponse status)
        {
            this.lastKnownStatus = status;
        }

        /// <summary>
        /// 在新的 Silo 接受请求后，将此调用的目标移至现在拥有该请求的 Silo。
        /// </summary>
        /// <param name="targetSilo">当前承载请求的 Silo。</param>
        /// <param name="forwardCount">请求在 <paramref name="targetSilo"/> 处的转发次数，用于确定同一调用的多次目标变更顺序。</param>
        /// <param name="isDeadSilo">确定 Silo 是否已被声明死亡。如果 <paramref name="targetSilo"/> 已死亡，则调用失败。</param>
        /// <returns><see langword="true"/> 表示已变更调用目标；<see langword="false"/> 表示该次目标变更已被后续变更取代。</returns>
        /// <remarks>
        /// 与调用完成发生竞争的目标变更不会影响调用结果。它只会将已请求的取消传播到新目标。
        /// </remarks>
        public bool TryRetarget(SiloAddress targetSilo, int forwardCount, Func<SiloAddress, bool> isDeadSilo)
        {
            // 此调用拥有其消息，且只有此类会锁定该消息，因此可将消息本身用作锁，避免为每个待完成调用添加字段。
            lock (Message)
            {
                if (forwardCount <= Message.ForwardCount)
                {
                    return false;
                }

                Message.ForwardCount = forwardCount;
                Message.TargetSilo = targetSilo;
            }

            // 在观察取消状态之前发布新目标，使此处或 OnCancellation 中至少有一方通知新目标。
            Interlocked.MemoryBarrier();
            if ((Volatile.Read(ref _state) & StateCancellationRequested) != 0)
            {
                SignalCancellation();
            }

            // 新目标的故障清理只会完成它观察到的、以该 Silo 为目标的调用。
            // 成员资格管理先将 Silo 标记为死亡，再清理其调用，因此清理要么观察到此次目标变更，要么此处的检查观察到其死亡。
            if (isDeadSilo(targetSilo))
            {
                OnTargetSiloFail(targetSilo);
            }

            return true;
        }

        public bool IsExpired(long currentTimestamp)
        {
            var duration = currentTimestamp - _startTimestamp;
            return duration > GetResponseTimeoutTimestampTicks();
        }

        private long GetResponseTimeoutTimestampTicks()
        {
            var defaultResponseTimeout = _invokable?.GetDefaultResponseTimeout();
            if (defaultResponseTimeout.HasValue)
            {
                return shared.GetTimestampTicks(defaultResponseTimeout.Value);
            }

            return shared.ResponseTimeoutTimestampTicks;
        }

        private TimeSpan GetResponseTimeout() => _invokable?.GetDefaultResponseTimeout() ?? shared.ResponseTimeout;

        private string GetTargetGrainType()
        {
            var type = Message.TargetGrain.Type;
            return type.IsDefault ? "unknown" : type.ToString()!;
        }

        private void OnCancellation(CancellationToken cancellationToken)
        {
            Interlocked.Or(ref _state, StateCancellationRequested);

            // 如果启用了等待确认，则只需向远程 Grain 发出取消请求信号，
            // 然后返回。
            if (shared.WaitForCancellationAcknowledgement)
            {
                SignalCancellation();
                return;
            }

            // 否则立即取消请求，无需等待被调用方确认
            // 取消请求。仍然会向被调用方发出取消信号。
            if (!TryComplete())
            {
                return;
            }

            RecordElapsedTime();
            SignalCancellation();
            shared.Unregister(Message);
            shared.ApplicationRequests.OnAppRequestsCanceled(GetTargetGrainType());
            OrleansCallBackDataEvent.Instance.OnCanceled(Message);
            context.Complete(Response.FromException(new OperationCanceledException(cancellationToken)));
            DisposeCancellationRegistration();
        }

        public void OnTimeout()
        {
            if (!TryComplete())
            {
                return;
            }

            RecordElapsedTime();
            if (shared.CancelRequestOnTimeout)
            {
                // 在发出信号之前记录取消请求，以便并发发生的目标变更也能向新目标发出信号。
                Interlocked.Or(ref _state, StateCancellationRequested);
                SignalCancellation();
            }

            this.shared.Unregister(this.Message);
            DisposeCancellationRegistration();
            shared.ApplicationRequests.OnAppRequestsTimedOut(GetTargetGrainType());

            OrleansCallBackDataEvent.Instance.OnTimeout(this.Message);

            var msg = this.Message; // 本地工作副本。

            var statusMessage = lastKnownStatus is StatusResponse status ? $"Last known status is {status}. " : string.Empty;
            var timeout = GetResponseTimeout();
            LogTimeout(this.shared.Logger, timeout, msg, statusMessage);

            var exception = new TimeoutException($"Response did not arrive on time in {timeout} for message: {msg}. {statusMessage}");
            context.Complete(Response.FromException(exception));
        }

        /// <summary>
        /// 因 <paramref name="failedSilo"/> 变得不可用而使调用失败，除非调用此后已改为指向另一个 Silo。
        /// </summary>
        public void OnTargetSiloFail(SiloAddress failedSilo)
        {
            // 在此锁的保护下变更调用目标，因此只有仍以故障 Silo 为目标的调用才会失败。
            lock (Message)
            {
                if (!failedSilo.Equals(Message.TargetSilo) || !TryComplete())
                {
                    return;
                }
            }

            RecordElapsedTime();
            this.shared.Unregister(this.Message);
            DisposeCancellationRegistration();

            OrleansCallBackDataEvent.Instance.OnTargetSiloFail(this.Message);
            var msg = this.Message;
            var statusMessage = lastKnownStatus is StatusResponse status ? $"Last known status is {status}. " : string.Empty;
            LogTargetSiloFail(this.shared.Logger, msg, statusMessage, Constants.TroubleshootingHelpLink);
            var exception = new SiloUnavailableException($"The target silo became unavailable for message: {msg}. {statusMessage}See {Constants.TroubleshootingHelpLink} for troubleshooting help.");
            this.context.Complete(Response.FromException(exception));
        }

        public void OnHostShutdown()
        {
            if (!TryComplete())
            {
                return;
            }

            RecordElapsedTime();
            this.shared.Unregister(this.Message);
            DisposeCancellationRegistration();

            var msg = this.Message;
            var exception = new SiloUnavailableException($"The local Orleans host is shutting down and can no longer process the request: {msg}.");
            this.context.Complete(Response.FromException(exception));
        }

        public void DoCallback(Message response)
        {
            if (!TryComplete())
            {
                return;
            }

            OrleansCallBackDataEvent.Instance.DoCallback(this.Message);

            RecordElapsedTime();
            DisposeCancellationRegistration();

            // 在 CallbackData 锁外执行回调。为这个无关的操作持有锁并不是良好实践。
            ResponseCallback(response, this.context);
        }

        private bool TryComplete() => (Interlocked.Or(ref _state, StateCompleted) & StateCompleted) == 0;

        private void RecordElapsedTime()
        {
            if (shared.ApplicationRequests.AppRequestsLatencyEnabled)
            {
                var elapsedMilliseconds = shared.TimeProvider.GetElapsedTime(_startTimestamp).TotalMilliseconds;
                shared.ApplicationRequests.OnAppRequestsEnd(elapsedMilliseconds);
            }
        }

        private void DisposeCancellationRegistration()
        {
            // 如果注册仍未完成，则发布该注册的一方会观察到调用完成，并释放该注册。
            if ((Volatile.Read(ref _state) & StateCancellationRegistrationPublished) != 0)
            {
                _cancellationTokenRegistration.Dispose();
            }
        }

        private static void ResponseCallback(Message message, IResponseCompletionSource context)
        {
            try
            {
                var body = message.BodyObject;
                if (body is Response response)
                {
                    context.Complete(response);
                }
                else
                {
                    HandleRejectionResponse(context, body as RejectionResponse);
                }
            }
            catch (Exception exc)
            {
                // 捕获异常，并用该异常使异步结果进入失败状态。
                context.Complete(Response.FromException(exc));
            }

            static void HandleRejectionResponse(IResponseCompletionSource context, RejectionResponse? rejection)
            {
                Exception exception;
                if (rejection?.RejectionType is Message.RejectionTypes.GatewayTooBusy)
                {
                    exception = new GatewayTooBusyException();
                }
                else
                {
                    exception = rejection?.Exception ?? new OrleansMessageRejectionException(rejection?.RejectionInfo ?? "Unable to send request - no rejection info available");
                }

                context.Complete(Response.FromException(exception));
            }
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100157,
            Level = LogLevel.Warning,
            Message = "Response did not arrive on time in '{Timeout}' for message: '{Message}'. {StatusMessage}About to break its promise."
        )]
        private static partial void LogTimeout(ILogger logger, TimeSpan timeout, Message message, string statusMessage);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100157,
            Level = LogLevel.Warning,
            Message = "The target silo became unavailable for message: '{Message}'. {StatusMessage}See {TroubleshootingHelpLink} for troubleshooting help. About to break its promise."
        )]
        private static partial void LogTargetSiloFail(ILogger logger, Message message, string statusMessage, string troubleshootingHelpLink);
    }
}

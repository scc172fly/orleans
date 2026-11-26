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

        // 请求的引用描述其取消和响应超时。 信件在发送后, 身体会发生变化 :
        // 重新审阅的信件保留其序列体,一旦重试发送后即予以释放。
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

        public Message Message { get; } // 可能持有应急管道使用的元数据

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
            // 只取消荣誉取消标记的请求 。
            // 并非所有目标支持 IGrainCallCancellationExtension,因此在这种情况下发送取消通知可能导致错误。
            // 如果目标方法不支持取消,则有可能取消基础设施层的要求。
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
        /// 将此呼叫的目标移动到 silo 之后,该申请即为所有者 silo 已经接受了
        /// </summary>
        /// <param name="targetSilo">下 silo 请求所在的当前主机。</param>
        /// <param name="forwardCount">请求的前方数 <paramref name="targetSilo"/>,命令重新瞄准同一呼叫。</param>
        /// <param name="isDeadSilo">确定一个 silo 是否被宣布为已死亡。如果 <paramref name="targetSilo"/> 已经死亡,该呼叫失败。</param>
        /// <returns><see langword="true"/> 如果调用目标重新瞄准; <see langword="false"/> 如果重用目标被取代。</returns>
        /// <remarks>
        /// 重新瞄准完成的比赛对呼叫结果没有影响, 它只会向新目标宣传取消请求。
        /// </remarks>
        public bool TryRetarget(SiloAddress targetSilo, int forwardCount, Func<SiloAddress, bool> isDeadSilo)
        {
            // 电话拥有它的信息, 只有这个类才锁定它, 所以信息可以作为锁, 而不给每个待拨的电话添加字段 。
            lock (Message)
            {
                if (forwardCount <= Message.ForwardCount)
                {
                    return false;
                }

                Message.ForwardCount = forwardCount;
                Message.TargetSilo = targetSilo;
            }

            // 在观测取消前先公布新目标,这样,要么这个目标,要么 OnCancellation 表示新目标。
            Interlocked.MemoryBarrier();
            if ((Volatile.Read(ref _state) & StateCancellationRequested) != 0)
            {
                SignalCancellation();
            }

            // 新目标的失灵扫描只完成它观察到的针对 silo的通话。
            // 成员 silo 搜索之前死前打扫, 所以扫瞄观察这个目标重瞄准 或者检查观察死亡。
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

            // 如果启用等待确认功能, 只需向远程 grain 信号即取消
            // 请求并返回。
            if (shared.WaitForCancellationAcknowledgement)
            {
                SignalCancellation();
                return;
            }

            // 否则,立即取消请求,不等待被传唤人确认
            // 请求取消。 仍然会发出被调用者的信号 。
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
                // 在发出信号前先记录请求,这样同时重新瞄准的目标也表示其新目标。
                Interlocked.Or(ref _state, StateCancellationRequested);
                SignalCancellation();
            }

            this.shared.Unregister(this.Message);
            DisposeCancellationRegistration();
            shared.ApplicationRequests.OnAppRequestsTimedOut(GetTargetGrainType());

            OrleansCallBackDataEvent.Instance.OnTimeout(this.Message);

            var msg = this.Message; // 当地工作本

            var statusMessage = lastKnownStatus is StatusResponse status ? $"Last known status is {status}. " : string.Empty;
            var timeout = GetResponseTimeout();
            LogTimeout(this.shared.Logger, timeout, msg, statusMessage);

            var exception = new TimeoutException($"Response did not arrive on time in {timeout} for message: {msg}. {statusMessage}");
            context.Complete(Response.FromException(exception));
        }

        /// <summary>
        /// 调用失败,原因是 <paramref name="failedSilo"/> 无法接听,除非调用目标已改为另一个 silo。
        /// </summary>
        public void OnTargetSiloFail(SiloAddress failedSilo)
        {
            // 重新瞄准目标时改变此锁定下的目标, 所以只有当仍然针对失败的 silo时呼叫才会失败 。
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

            // 将您调回 CallbackData 锁外。 只是没有为此不相关的操作锁定好的做法 。
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
            // 如果登记仍有待办理,其出版商将观察完成并处理。
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
                // 抓住例外,然后用它来打破承诺。
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

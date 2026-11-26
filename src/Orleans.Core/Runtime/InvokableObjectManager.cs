using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;

namespace Orleans
{
    internal sealed partial class InvokableObjectManager
    {
        private readonly ConcurrentDictionary<ObserverGrainId, LocalObjectData> localObjects = new ConcurrentDictionary<ObserverGrainId, LocalObjectData>();

        private readonly InterfaceToImplementationMappingCache _interfaceToImplementationMapping;
        private readonly IGrainContext rootGrainContext;
        private readonly IRuntimeClient runtimeClient;
        private readonly ILogger logger;
        private readonly DeepCopier deepCopier;
        private readonly DeepCopier<Response> _responseCopier;
        private readonly MessagingTrace messagingTrace;
        private readonly AdmissionGate _invocations = new();
        private readonly AdmissionGate _cancellations = new();
        private List<IIncomingGrainCallFilter>? _grainCallFilters;

        private List<IIncomingGrainCallFilter> GrainCallFilters
            => _grainCallFilters ??= [.. runtimeClient.ServiceProvider.GetServices<IIncomingGrainCallFilter>()];

        public InvokableObjectManager(
            IGrainContext rootGrainContext,
            IRuntimeClient runtimeClient,
            DeepCopier deepCopier,
            MessagingTrace messagingTrace,
            DeepCopier<Response> responseCopier,
            InterfaceToImplementationMappingCache interfaceToImplementationMapping,
            ILogger logger)
        {
            this.rootGrainContext = rootGrainContext;
            this.runtimeClient = runtimeClient;
            this.deepCopier = deepCopier;
            this.messagingTrace = messagingTrace;
            _responseCopier = responseCopier;
            _interfaceToImplementationMapping = interfaceToImplementationMapping;
            this.logger = logger;
        }

        public bool TryRegister(IAddressable obj, ObserverGrainId objectId)
        {
            return this.localObjects.TryAdd(objectId, new LocalObjectData(obj, objectId, this));
        }

        public bool TryDeregister(ObserverGrainId objectId)
        {
            if (!this.localObjects.TryRemove(objectId, out var objectData))
            {
                return false;
            }

            objectData.OnDeregistered();
            return true;
        }

        /// <summary>
        /// 关闭观察员的入职和排水处理,包括取消管制和应答处理。
        /// </summary>
        public async Task StopAsync()
        {
            // 取消管制可在申请受理关闭时解除工作。
            await _invocations.CloseAsync().ConfigureAwait(false);
            await _cancellations.CloseAsync().ConfigureAwait(false);
        }

        internal static bool IsCancellationRequest(Message message) =>
            message.BodyObject is IInvokable request && request.GetInterfaceType() == typeof(IGrainCallCancellationExtension);

        private AdmissionGate GetAdmissionGate(Message message) =>
            IsCancellationRequest(message) ? _cancellations : _invocations;

        internal void RejectMessage(Message message)
        {
            LogRejectingMessageDuringShutdown(logger, message);
            if (message.Direction == Message.Directions.Request)
            {
                runtimeClient.SendResponse(message, Response.FromException(
                    new SiloUnavailableException("The local Orleans host is shutting down and can no longer accept observer invocations.")));
            }
        }

        public void Dispatch(Message message)
        {
            if (!ObserverGrainId.TryParse(message.TargetGrain, out var observerId))
            {
                LogNotAddressedToAnObserver(logger, message);
                return;
            }

            if (this.localObjects.TryGetValue(observerId, out var objectData))
            {
                objectData.ReceiveMessage(message);
            }
            else
            {
                LogUnexpectedTargetInRequest(logger, message.TargetGrain, message);
            }
        }

        public sealed partial class LocalObjectData : IGrainContext, IGrainCallCancellationExtension
        {
            private const int MaxPendingCancellations = 1_024;
            private static readonly Func<object?, Task> HandleFunc = self => ((LocalObjectData)self!).LocalObjectMessagePumpAsync();
            private readonly InvokableObjectManager _manager;
            private readonly Dictionary<(GrainId SenderGrainId, CorrelationId MessageId), LinkedListNode<(GrainId, CorrelationId)>> _pendingCancellations = [];
            private readonly LinkedList<(GrainId SenderGrainId, CorrelationId MessageId)> _pendingCancellationOrder = [];
            private readonly Dictionary<Message, Task?> _runningRequests = [];
            private Task? _messagePumpTask;

            internal LocalObjectData(IAddressable obj, ObserverGrainId observerId, InvokableObjectManager manager)
            {
                this.LocalObject = new WeakReference(obj);
                this.ObserverId = observerId;
                this.Messages = new Queue<Message>();
                this.Running = false;
                _manager = manager;
            }

            internal WeakReference LocalObject { get; }
            internal ObserverGrainId ObserverId { get; }
            internal Queue<Message> Messages { get; }
            internal bool Running { get; set; }

            GrainId IGrainContext.GrainId => this.ObserverId.GrainId;

            GrainReference IGrainContext.GrainReference =>
                _manager.runtimeClient.InternalGrainFactory.GetGrain(ObserverId.GrainId).AsReference();

            object? IGrainContext.GrainInstance => this.LocalObject.Target;

            ActivationId IGrainContext.ActivationId => throw new NotImplementedException();

            GrainAddress IGrainContext.Address => throw new NotImplementedException();

            IServiceProvider IGrainContext.ActivationServices => throw new NotSupportedException();

            IGrainLifecycle IGrainContext.ObservableLifecycle => throw new NotImplementedException();

            public IWorkItemScheduler Scheduler => throw new NotImplementedException();

            void IGrainContext.SetComponent<TComponent>(TComponent? value) where TComponent : class
            {
                if (this.LocalObject.Target is TComponent)
                {
                    throw new ArgumentException("Cannot override a component which is implemented by this grain");
                }

                _manager.rootGrainContext.SetComponent(value);
            }

            public object? GetComponent(Type componentType)
            {
                if (componentType.IsAssignableFrom(this.LocalObject.Target?.GetType()))
                {
                    return LocalObject.Target;
                }
                else if (componentType.IsAssignableFrom(GetType()))
                {
                    return this;
                }

                return _manager.rootGrainContext.GetComponent(componentType);
            }

            public object? GetTarget() => this.LocalObject.Target;

            bool IEquatable<IGrainContext>.Equals(IGrainContext? other) => ReferenceEquals(this, other);

            public void ReceiveMessage(object msg)
            {
                var message = (Message)msg;
                var gate = _manager.GetAdmissionGate(message);
                if (!gate.TryEnterUnscoped())
                {
                    _manager.RejectMessage(message);
                    return;
                }

                var ownsAdmission = true;
                try
                {
                    // 将招生所有权转让给泵或间断性职业。
                    ReceiveMessage(message, ref ownsAdmission);
                }
                finally
                {
                    if (ownsAdmission)
                    {
                        gate.Exit();
                    }
                }
            }

            private void ReceiveMessage(Message message, ref bool ownsAdmission)
            {
                var obj = this.LocalObject.Target;
                if (obj is null)
                {
                    // 从字典记录中删除垃圾收集对象的收藏记录吗? 但现在我们无法检测无效发送 ID 。
                    LogObserverGarbageCollected(_manager.logger, this.ObserverId, message);
                    // 试着移走它,如果它不在那里,我们不在乎
                    _manager.TryDeregister(this.ObserverId);
                    message.Dispose();
                    return;
                }

                // 立即处理 AlwaysInterleave 信件( 如取消请求) , 不排队 。
                // 即使目前正在处理另一项请求,这些讯息也需要立即处理。
                if (message.IsAlwaysInterleave)
                {
                    bool wasCancelled;
                    lock (Messages)
                    {
                        wasCancelled = RemovePendingCancellation((message.SendingGrain, message.Id));
                        if (!wasCancelled)
                        {
                            // 跟踪运行中的请求, 以便取消它 。
                            var task = Task.Factory.StartNew(
                                static state =>
                                {
                                    var (self, msg) = ((LocalObjectData, Message))state!;
                                    return self.ProcessMessageAsync(msg);
                                },
                                (this, message),
                                CancellationToken.None,
                                TaskCreationOptions.DenyChildAttach,
                                TaskScheduler.Default).Unwrap();
                            ownsAdmission = false;
                            _runningRequests.Add(message, task);
                        }
                    }

                    if (wasCancelled)
                    {
                        SendCanceledResponse(message);
                    }

                    return;
                }

                bool start;
                bool wasWaitingCancellation;
                lock (this.Messages)
                {
                    wasWaitingCancellation = RemovePendingCancellation((message.SendingGrain, message.Id));
                    if (wasWaitingCancellation)
                    {
                        start = false;
                    }
                    else
                    {
                        this.Messages.Enqueue(message);
                        ownsAdmission = false;
                        start = !this.Running;
                        this.Running = true;
                    }
                }

                if (wasWaitingCancellation)
                {
                    SendCanceledResponse(message);
                    return;
                }

                LogInvokeLocalObjectAsync(_manager.logger, message, start);

                if (start)
                {
                    // 我们希望确保信息泵的运行 同步
                    // 当前线条的当前线索。 见
                    // http://channel9.msdn.com/Events/TechEd/Europe/2013/DEV-B317#fbid=aIWUq0ssW74
                    // 位置54: 45。
                    //
                    // 根据以下网站公布的信息:
                    // http://stackoverflow.com/questions/12245935/is-task-factory-startnew-guaranteed-to-use-another-thread-than-the-calling-thr
                    // 这个语系取决于 TaskScheduler 尚未执行
                    // 将 QueueTask 替换为任务内衬( 而不是队列) 。 这似乎似乎 。
                    // 提交人认为不可信,因为 .NET 排期者都没有这样做,而且
                    // 被认为不健全的形式( OrleansTaskScheduler 不这样做)。
                    //
                    // 如果出于某种原因,这不正确 我们可以保证
                    // 将占位符继续符号通过 将占位符的延续符标记切换到 Task.StartNew()
                    // i.e替换为:
                    //
                    // return Task.StartNew(() => ..., new CancellationToken());
                    // 我们把这些选项传给 Task.Factory.StartNew ,因为它们的调用完全相同
                    // Task.Run。见: https://blogs.msdn.microsoft.com/pfxteam/2011/10/24/task-run-vs-task-factory-startnew/
                    _messagePumpTask = Task.Factory.StartNew(
                            HandleFunc,
                            this,
                            CancellationToken.None,
                            TaskCreationOptions.DenyChildAttach,
                            TaskScheduler.Default).Unwrap();
                }
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Reliability",
                "CA2000:Dispose objects before losing scope",
                Justification = "Each dequeued message is transferred to ProcessMessageAsync, which completes or rejects the request.")]
            private async Task LocalObjectMessagePumpAsync()
            {
                while (TryDequeueMessage(out var message))
                {
                    await ProcessMessageAsync(message);
                }

                bool TryDequeueMessage([NotNullWhen(true)] out Message? message)
                {
                    lock (Messages)
                    {
                        var result = Messages.TryDequeue(out message);
                        if (!result)
                        {
                            Running = false;
                        }
                        else
                        {
                            _runningRequests.Add(message!, _messagePumpTask);
                        }

                        return result;
                    }
                }
            }

            private async Task ProcessMessageAsync(Message message)
            {
                try
                {
                    if (message.IsExpired)
                    {
                        _manager.messagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Invoke);
                        message.Dispose();
                        return;
                    }

                    if (message.RequestContextData is { Count: > 0 })
                    {
                        RequestContextExtensions.Import(message.RequestContextData);
                    }

                    IInvokable? request;
                    try
                    {
                        if (message.BodyObject is not IInvokable invokableBody)
                        {
                            _manager.runtimeClient.SendResponse(
                                message,
                                Response.FromException(new InvalidOperationException("Message body is not an invokable request")));
                            return;
                        }

                        request = invokableBody;
                    }
                    catch (Exception deserializationException)
                    {
                        LogErrorDeserializingMessageBody(_manager.logger, deserializationException, message);
                        _manager.runtimeClient.SendResponse(message, Response.FromException(deserializationException));
                        return;
                    }

                    try
                    {
                        request.SetTarget(this);
                        if (TryTakePendingCancellation(message))
                        {
                            TryCancelInvokable(request);
                        }

                        var filters = _manager.GrainCallFilters;
                        Response response;
                        if (filters is { Count: > 0 } || LocalObject.Target is IIncomingGrainCallFilter)
                        {
                            var invoker = new GrainMethodInvoker(message, this, request, filters, _manager._interfaceToImplementationMapping, _manager._responseCopier);
                            await invoker.Invoke();
                            response = invoker.Response!;
                        }
                        else
                        {
                            response = await request.Invoke();
                            // 复印机保留其输入的无效状态。
                            response = _manager._responseCopier.Copy(response)!;
                        }

                        if (message.Direction != Message.Directions.OneWay)
                        {
                            this.SendResponseAsync(message, response);
                        }
                    }
                    catch (Exception exc)
                    {
                        this.ReportException(message, exc);
                    }
                }
                catch (Exception outerException)
                {
                    // 忽略并不断循环。
                    LogErrorProcessingMessage(_manager.logger, outerException, message);
                }
                finally
                {
                    lock (Messages)
                    {
                        _runningRequests.Remove(message);
                    }

                    // 生成的接口元数据在整个请求期内保持稳定。
                    _manager.GetAdmissionGate(message).Exit();
                }
            }

            internal void OnDeregistered()
            {
                lock (Messages)
                {
                    _pendingCancellations.Clear();
                    _pendingCancellationOrder.Clear();
                }
            }

            private bool TryTakePendingCancellation(Message message)
            {
                lock (Messages)
                {
                    return RemovePendingCancellation((message.SendingGrain, message.Id));
                }
            }

            private bool TryCancelInvokable(IInvokable request)
            {
                try
                {
                    return request.TryCancel();
                }
                catch (Exception exception)
                {
                    LogErrorCancellationCallbackFailed(_manager.logger, exception);
                    return true;
                }
            }

            private void SendCanceledResponse(Message message) =>
                _manager.runtimeClient.SendResponse(message, Response.FromException(new OperationCanceledException()));

            private void SendResponseAsync(Message message, Response resultObject)
            {
                if (message.IsExpired)
                {
                    _manager.messagingTrace.OnDropExpiredMessage(message, MessagingInstruments.Phase.Respond);
                    return;
                }

                Response deepCopy;
                try
                {
                    // 如果深度副本失败,我们预期会通知打电话者。
                    // 复印机保留其输入的无效状态。
                    deepCopy = _manager.deepCopier.Copy(resultObject)!;
                }
                catch (Exception exc2)
                {
                    _manager.runtimeClient.SendResponse(message, Response.FromException(exc2));
                    LogErrorSendingResponse(_manager.logger, exc2);
                    return;
                }

                // 深影成功
                _manager.runtimeClient.SendResponse(message, deepCopy);
                return;
            }

            private void ReportException(Message message, Exception exception)
            {
                switch (message.Direction)
                {
                    case Message.Directions.OneWay:
                        LogErrorInvokingOneWayRequest(_manager.logger, exception, message.BodyObject?.ToString(), message.InterfaceType);
                        break;

                    case Message.Directions.Request:
                        Exception deepCopy;
                        try
                        {
                            // 如果深度副本失败,我们预期会通知打电话者。
                            // 复印机保留其输入的无效状态。
                            deepCopy = _manager.deepCopier.Copy(exception)!;
                        }
                        catch (Exception ex2)
                        {
                            _manager.runtimeClient.SendResponse(message, Response.FromException(ex2));
                            LogErrorSendingExceptionResponse(_manager.logger, ex2);
                            return;
                        }

                        // 深影成功
                        var response = Response.FromException(deepCopy);
                        _manager.runtimeClient.SendResponse(message, response);
                        break;

                    default:
                        throw new InvalidOperationException($"Unrecognized direction for message {message}, which resulted in exception: {exception}");
                }
            }

            public void Activate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken) { }
            public void Deactivate(DeactivationReason deactivationReason, CancellationToken cancellationToken) { }

            public void Rehydrate(IRehydrationContext context)
            {
                // 移民得不到支持,但是如果有背景情况的话,我们需要处理它。
                (context as IDisposable)?.Dispose();
            }

            public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
            {
                // 移民得不到支持,什么都不要做:合同是这种方式试图移徙,但不能保证移徙会发生。
            }

            ValueTask IGrainCallCancellationExtension.CancelRequestAsync(
                GrainId senderGrainId,
                CorrelationId messageId,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryCancelRequest();
                return ValueTask.CompletedTask;

                void TryCancelRequest()
                {
                    Message? message = null;
                    var wasWaiting = false;
                    var key = (senderGrainId, messageId);
                    lock (Messages)
                    {
                        // 检查运行中的请求 。
                        foreach (var runningRequest in _runningRequests.Keys)
                        {
                            if (runningRequest.Id == messageId && runningRequest.SendingGrain == senderGrainId)
                            {
                                message = runningRequest;
                                break;
                            }
                        }

                        if (message is null)
                        {
                            // 检查等待请求
                            foreach (var waitingRequest in Messages)
                            {
                                var waiting = waitingRequest;
                                if (waiting.Id == messageId && waiting.SendingGrain == senderGrainId)
                                {
                                    message = waiting;
                                    wasWaiting = true;

                                    // 删除信件, 因为信件会立即被拒绝( 在锁外) 而不执行 。
                                    var initialCount = Messages.Count;
                                    for (var i = 0; i < initialCount; i++)
                                    {
                                        var current = Messages.Dequeue();
                                        if (!ReferenceEquals(current, message))
                                        {
                                            Messages.Enqueue(current);
                                        }
                                    }

                                    break;
                                }
                            }
                        }

                        if (!wasWaiting)
                        {
                            // 注销和援引独立交付。
                            // 直到匹配的引用被接受或初始化了本地标记。
                            RetainPendingCancellation(key);
                        }
                    }

                    var didCancel = false;
                    if (message is not null)
                    {
                        // 信件从未开始执行, 所以立即发送取消回复 。
                        // 如果信件真的开始执行, 请等待它观察取消的标记并自答 。
                        if (wasWaiting)
                        {
                            try
                            {
                                SendCanceledResponse(message);
                            }
                            finally
                            {
                                _manager.GetAdmissionGate(message).Exit();
                            }

                            didCancel = true;
                        }
                        else if (message.BodyObject is IInvokable invokableRequest)
                        {
                            didCancel = TryCancelInvokable(invokableRequest) || !invokableRequest.IsCancellable;
                        }
                        else
                        {
                            // 假设请求无法撤销。
                            didCancel = true;
                        }
                    }

                    if (didCancel)
                    {
                        lock (Messages)
                        {
                            RemovePendingCancellation(key);
                        }
                    }
                }
            }

            private void RetainPendingCancellation((GrainId SenderGrainId, CorrelationId MessageId) key)
            {
                if (_pendingCancellations.ContainsKey(key))
                {
                    return;
                }

                if (_pendingCancellations.Count >= MaxPendingCancellations)
                {
                    var oldest = _pendingCancellationOrder.First!;
                    _pendingCancellationOrder.RemoveFirst();
                    _pendingCancellations.Remove(oldest.Value);
                }

                _pendingCancellations.Add(key, _pendingCancellationOrder.AddLast(key));
            }

            private bool RemovePendingCancellation((GrainId SenderGrainId, CorrelationId MessageId) key)
            {
                if (!_pendingCancellations.Remove(key, out var node))
                {
                    return false;
                }

                _pendingCancellationOrder.Remove(node);
                return true;
            }

            [LoggerMessage(
                Level = LogLevel.Warning,
                Message = "One or more cancellation callbacks failed."
            )]
            private static partial void LogErrorCancellationCallbackFailed(ILogger logger, Exception exception);

            public Task Deactivated => Task.CompletedTask;
        }

        private readonly struct ObserverGrainIdLogValue(ObserverGrainId observerId)
        {
            public override string ToString() => observerId.ToString();
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Rejecting observer invocation during shutdown: '{Message}'."
        )]
        private static partial void LogRejectingMessageDuringShutdown(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_OGC_TargetNotFound_2,
            Level = LogLevel.Error,
            Message = "Message is not addressed to an observer. Message: '{Message}'."
        )]
        private static partial void LogNotAddressedToAnObserver(ILogger logger, Message message);

        [LoggerMessage(
            EventId = (int)ErrorCode.ProxyClient_OGC_TargetNotFound,
            Level = LogLevel.Error,
            Message = "Unexpected target grain in request: {TargetGrain}. Message: '{Message}'."
        )]
        private static partial void LogUnexpectedTargetInRequest(ILogger logger, GrainId targetGrain, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.Runtime_Error_100162,
            Message = "Object associated with id '{ObserverId}' has been garbage collected. Deleting object reference and unregistering it. Message: '{Message}'."
        )]
        private static partial void LogObserverGarbageCollected(ILogger logger, ObserverGrainId observerId, Message message);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "InvokeLocalObjectAsync '{Message}' start '{Start}'."
        )]
        private static partial void LogInvokeLocalObjectAsync(ILogger logger, Message message, bool start);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.ProxyClient_OGC_SendResponseFailed,
            Message = "Error sending a response."
        )]
        private static partial void LogErrorSendingResponse(ILogger logger, Exception exc);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.ProxyClient_OGC_UnhandledExceptionInOneWayInvoke,
            Message = "Exception during invocation of notification '{Request}', interface '{Interface}'. Ignoring exception because this is a one way request."
        )]
        private static partial void LogErrorInvokingOneWayRequest(ILogger logger, Exception exception, string? request, GrainInterfaceType @interface);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.ProxyClient_OGC_SendExceptionResponseFailed,
            Message = "Error sending an exception response."
        )]
        private static partial void LogErrorSendingExceptionResponse(ILogger logger, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception during message body deserialization for message: '{Message}'."
        )]
        private static partial void LogErrorDeserializingMessageBody(ILogger logger, Exception exception, Message message);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception processing message '{Message}'."
        )]
        private static partial void LogErrorProcessingMessage(ILogger logger, Exception exception, Message message);
    }
}

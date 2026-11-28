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
        /// 关闭观察者调用准入，并排空调用处理流程，包括取消控制消息及响应处理。
        /// </summary>
        public async Task StopAsync()
        {
            // 应用请求准入已关闭时，取消控制消息仍可以使已准入的工作结束。
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
                    // 将请求发布到队列时，把准入许可的所有权转移给消息泵或交错执行的调用。
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
                    // 要从字典中移除已被垃圾回收的对象对应的记录吗？这样将无法再检测无效的分派 ID。
                    LogObserverGarbageCollected(_manager.logger, this.ObserverId, message);
                    // 尝试移除。如果记录不存在，则无需处理。
                    _manager.TryDeregister(this.ObserverId);
                    message.Dispose();
                    return;
                }

                // 立即处理 AlwaysInterleave 消息（例如取消请求），不将其排队。
                // 即使当前有其他请求正在运行，也必须立即处理这些消息。
                if (message.IsAlwaysInterleave)
                {
                    bool wasCancelled;
                    lock (Messages)
                    {
                        wasCancelled = RemovePendingCancellation((message.SendingGrain, message.Id));
                        if (!wasCancelled)
                        {
                            // 跟踪正在运行的请求，以便取消它。
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
                    // 我们希望确保消息泵相对于
                    // 当前线程异步运行。参见
                    // http://channel9.msdn.com/Events/TechEd/Europe/2013/DEV-B317#fbid=aIWUq0ssW74
                    // 54:45 处。
                    //
                    // 根据以下位置发布的信息：
                    // http://stackoverflow.com/questions/12245935/is-task-factory-startnew-guaranteed-to-use-another-thread-than-the-calling-thr
                    // 此惯用写法依赖于 TaskScheduler 不将重写的
                    // QueueTask 实现为任务内联执行（而不是排队）。作者认为这种情况
                    // 不太可能发生，因为 .NET 的所有调度器都不这样做，而且
                    // 这被认为是不良实践（OrleansTaskScheduler 不这样做）。
                    //
                    // 如果出于某种原因这一假设不成立，则可以保证所需行为，
                    // 方法是改为向 Task.StartNew() 传递一个占位的延续令牌，
                    // 即：
                    //
                    // return Task.StartNew(() => ..., new CancellationToken());
                    // 我们向 Task.Factory.StartNew 传递这些选项，因为它们使此调用
                    // 等同于 Task.Run。参见：https://blogs.msdn.microsoft.com/pfxteam/2011/10/24/task-run-vs-task-factory-startnew/
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
                            // 复制器保留其输入的 null 状态。
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
                    // 忽略并继续循环。
                    LogErrorProcessingMessage(_manager.logger, outerException, message);
                }
                finally
                {
                    lock (Messages)
                    {
                        _runningRequests.Remove(message);
                    }

                    // 生成的接口元数据在请求的整个生命周期内保持稳定。
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
                    // 如果深拷贝失败，应通知调用方。
                    // 复制器保留其输入的 null 状态。
                    deepCopy = _manager.deepCopier.Copy(resultObject)!;
                }
                catch (Exception exc2)
                {
                    _manager.runtimeClient.SendResponse(message, Response.FromException(exc2));
                    LogErrorSendingResponse(_manager.logger, exc2);
                    return;
                }

                // 深拷贝成功。
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
                            // 如果深拷贝失败，应通知调用方。
                            // 复制器保留其输入的 null 状态。
                            deepCopy = _manager.deepCopier.Copy(exception)!;
                        }
                        catch (Exception ex2)
                        {
                            _manager.runtimeClient.SendResponse(message, Response.FromException(ex2));
                            LogErrorSendingExceptionResponse(_manager.logger, ex2);
                            return;
                        }

                        // 深拷贝成功。
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
                // 不支持迁移，但如果提供了上下文，则需要释放它。
                (context as IDisposable)?.Dispose();
            }

            public void Migrate(Dictionary<string, object>? requestContext, CancellationToken cancellationToken)
            {
                // 不支持迁移。不执行任何操作：契约规定此方法会尝试迁移，但不保证迁移一定发生。
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
                        // 检查正在运行的请求。
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
                            // 检查等待中的请求。
                            foreach (var waitingRequest in Messages)
                            {
                                var waiting = waitingRequest;
                                if (waiting.Id == messageId && waiting.SendingGrain == senderGrainId)
                                {
                                    message = waiting;
                                    wasWaiting = true;

                                    // 移除此消息，因为它会立即在锁外被拒绝，而不会执行。
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
                            // 取消消息和调用请求分别独立投递。保留取消记录，
                            // 直到匹配的调用已获准进入或已初始化其本地令牌。
                            RetainPendingCancellation(key);
                        }
                    }

                    var didCancel = false;
                    if (message is not null)
                    {
                        // 此消息从未开始执行，因此立即发送表示已取消的响应。
                        // 如果消息已经开始执行，则等待其观察到取消令牌并自行响应。
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
                            // 假定该请求不可取消。
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

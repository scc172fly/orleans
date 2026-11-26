using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Orleans.CodeGeneration;
using Orleans.Serialization;

namespace Orleans.Runtime
{
    internal partial class MessageFactory
    {
        private static ulong _nextId;

        // none 降低给定的 grain 电离碰撞仅在假设情况下才相关。
        // 发生碰撞的微小机会可以接受,例如取消电话。
        private readonly ulong _seed;
        private readonly DeepCopier _deepCopier;
        private readonly ILogger _logger;

        public MessageFactory(DeepCopier deepCopier, ILogger<MessageFactory> logger, MessagingTrace _)
        {
            _deepCopier = deepCopier;
            _logger = logger;

            // 为主机生成64位元的无值,与每个邮件相关号组合,以获得独特的、每个主机的价值。
            // 这样可以避免特定主机在不同主机之间发生误撞 grain.
            _seed = unchecked((ulong)Random.Shared.NextInt64());
        }

        public Message CreateMessage(object? body, InvokeMethodOptions options) =>
            CreateMessage(body, options, RequestContextExtensions.Export(_deepCopier));

        public Message CreateMessage(object? body, InvokeMethodOptions options, Dictionary<string, object>? requestContextData)
        {
            var message = new Message
            {
                Direction = (options & InvokeMethodOptions.OneWay) != 0 ? Message.Directions.OneWay : Message.Directions.Request,
                Id = GetNextCorrelationId(),
                IsReadOnly = (options & InvokeMethodOptions.ReadOnly) != 0,
                IsUnordered = (options & InvokeMethodOptions.Unordered) != 0,
                IsAlwaysInterleave = (options & InvokeMethodOptions.AlwaysInterleave) != 0,
                BodyObject = body,
                RequestContextData = requestContextData,
            };

            return message;
        }

        private CorrelationId GetNextCorrelationId()
        {
            var id = _seed ^ Interlocked.Increment(ref _nextId);
            return new CorrelationId(unchecked((long)id));
        }

        // 收到 grain 的呼叫带有接口类型。 本地请求共享此类型, 并调用过滤器来观察每个请求的自定义界面名称 。
        private static readonly GrainInterfaceType LocalRequestInterfaceType = GrainInterfaceType.Create("Orleans.Runtime.ILocalRequest");

        /// <summary>
        /// 创建单向请求,在自己身上设置 <paramref name="target"/> 队列。
        /// </summary>
        /// <remarks>
        /// 请求启动一个新的呼叫链, 永远不过期, 并在 <paramref name="target"/> 上完成或被拒绝 。
        /// 仅本地信息总是带有 <see cref="ILocalRequest"/> 体。
        /// </remarks>
        public Message CreateLocalRequest(IGrainContext target, ILocalRequest request, LocalRequestOptions options)
        {
            ArgumentNullException.ThrowIfNull(request);
            var siloAddress = target.Address.SiloAddress;
            var message = CreateMessage(request, InvokeMethodOptions.OneWay, requestContextData: null);
            message.SetInfiniteTimeToLive();
            message.SendingGrain = target.GrainId;
            message.TargetGrain = target.GrainId;
            message.SendingSilo = siloAddress;
            message.TargetSilo = siloAddress;
            message.InterfaceType = LocalRequestInterfaceType;
            message.IsKeepAlive = options.KeepAlive;
            message.IsAlwaysInterleave = options.Interleave;
            message.IsLocalOnly = true;
            return message;
        }

        public Message CreateResponseMessage(Message request)
        {
            var response = new Message
            {
                IsSystemMessage = request.IsSystemMessage,
                Direction = Message.Directions.Response,
                Id = request.Id,
                IsReadOnly = request.IsReadOnly,
                IsAlwaysInterleave = request.IsAlwaysInterleave,
                TargetSilo = request.SendingSilo,
                TargetGrain = request.SendingGrain,
                SendingSilo = request.TargetSilo,
                SendingGrain = request.TargetGrain,
                CacheInvalidationHeader = request.CacheInvalidationHeader,
                RequestContextData = RequestContextExtensions.Export(_deepCopier),
            };

            response.SetTimeToLive(request);
            return response;
        }

        public Message CreateRejectionResponse(Message request, Message.RejectionTypes type, string info, Exception? ex = null)
        {
            var response = CreateResponseMessage(request);
            response.Result = Message.ResponseTypes.Rejection;
            response.BodyObject = new RejectionResponse
            {
                RejectionType = type,
                RejectionInfo = info,
                Exception = ex,
            };
            LogCreatingRejectionResponse(_logger, ex, type, info);
            return response;
        }

        internal Message CreateDiagnosticResponseMessage(Message request, bool isExecuting, bool isWaiting, List<string> diagnostics)
        {
            var response = CreateResponseMessage(request);
            response.Result = Message.ResponseTypes.Status;
            response.BodyObject = new StatusResponse(isExecuting, isWaiting, diagnostics);

            LogCreatingStatusUpdate(_logger, request, new(diagnostics));
            return response;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Creating '{RejectionType}' rejection with info '{Information}'."
        )]
        private static partial void LogCreatingRejectionResponse(ILogger logger, Exception? exception, Message.RejectionTypes rejectionType, string information);

        private readonly struct DiagnosticsLogValue(List<string> diagnostics)
        {
            public override string ToString() => string.Join(", ", diagnostics);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Creating '{RequestMessage}' status update with diagnostics '{Diagnostics}'"
        )]
        private static partial void LogCreatingStatusUpdate(ILogger logger, Message requestMessage, DiagnosticsLogValue diagnostics);
    }
}

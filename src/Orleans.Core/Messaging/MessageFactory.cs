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

        // 此随机数使给定 Grain 的 ID 冲突概率实际上接近于零。ID 冲突只涉及
        // 能够接受极小冲突概率的场景，例如调用取消。
        private readonly ulong _seed;
        private readonly DeepCopier _deepCopier;
        private readonly ILogger _logger;

        public MessageFactory(DeepCopier deepCopier, ILogger<MessageFactory> logger, MessagingTrace _)
        {
            _deepCopier = deepCopier;
            _logger = logger;

            // 为宿主生成 64 位随机数，与每条消息的关联 ID 组合，得到宿主范围内唯一的值。
            // 这可避免给定 Grain 在不同宿主之间发生 ID 冲突。
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

        // 传入的 Grain 调用携带接口类型。本地请求共享此接口类型，而调用筛选器观察各请求自身的接口名称。
        private static readonly GrainInterfaceType LocalRequestInterfaceType = GrainInterfaceType.Create("Orleans.Runtime.ILocalRequest");

        /// <summary>
        /// 创建由 <paramref name="target"/> 在自身队列中排队的单向请求。
        /// </summary>
        /// <remarks>
        /// 请求启动新的调用链，永不过期，并在 <paramref name="target"/> 上完成，或随其一并被拒绝。
        /// 仅本地使用的消息始终携带 <see cref="ILocalRequest"/> 消息体。
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

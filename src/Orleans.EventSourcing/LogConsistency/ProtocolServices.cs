using System;
using Microsoft.Extensions.Logging;
using Orleans.EventSourcing;
using Orleans.Serialization;

namespace Orleans.Runtime.LogConsistency
{
    /// <summary>
    /// 供运行分布式协议的日志视图适配器使用的功能。
    /// 此类使无法访问运行时内部实现的提供程序能够访问这些服务。
    /// 此类还保存特定于 Grain 的信息（例如 Grain 引用），并缓存
    /// </summary>
    internal partial class ProtocolServices : ILogConsistencyProtocolServices
    {
        private readonly ILogger log;
        private readonly DeepCopier deepCopier;
        private readonly IGrainContext grainContext;   // 指向拥有此服务对象的 Grain 的引用。

        public ProtocolServices(
            IGrainContext grainContext,
            ILoggerFactory loggerFactory,
            DeepCopier deepCopier,
            ILocalSiloDetails siloDetails)
        {
            this.grainContext = grainContext;
            this.log = loggerFactory.CreateLogger<ProtocolServices>();
            this.deepCopier = deepCopier;
            this.MyClusterId = siloDetails.ClusterId;
        }

        public GrainId GrainId => grainContext.GrainId;

        public string MyClusterId { get; }

        public T DeepCopy<T>(T value) => this.deepCopier.Copy(value)!;

        public void ProtocolError(string msg, bool throwexception)
        {
            if (throwexception)
            {
                LogErrorProtocolFatal(log, grainContext.GrainId, msg);
            }
            else
            {
                LogErrorProtocol(log, grainContext.GrainId, msg);
            }

            if (!throwexception)
                return;

            throw new OrleansException(string.Format("{0} (grain={1}, cluster={2})", msg, grainContext.GrainId, this.MyClusterId));
        }

        public void CaughtException(string where, Exception e)
        {
            LogErrorCaughtException(log, e, grainContext.GrainId, where);
        }

        public void CaughtUserCodeException(string callback, string where, Exception e)
        {
            LogWarningCaughtUserCodeException(log, e, grainContext.GrainId, callback, where);
        }

        public void Log(LogLevel level, string format, params object[] args)
        {
            if (log != null && log.IsEnabled(level))
            {
                var msg = $"{grainContext.GrainId} {string.Format(format, args)}";
                log.Log(level, 0, msg, null, (m, exc) => $"{m}");
            }
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.LogConsistency_ProtocolError,
            Level = LogLevel.Error,
            Message = "{GrainId} Protocol Error: {Message}"
        )]
        private static partial void LogErrorProtocol(ILogger logger, GrainId grainId, string message);

        [LoggerMessage(
            EventId = (int)ErrorCode.LogConsistency_ProtocolFatalError,
            Level = LogLevel.Error,
            Message = "{GrainId} Protocol Error: {Message}"
        )]
        private static partial void LogErrorProtocolFatal(ILogger logger, GrainId grainId, string message);

        [LoggerMessage(
            EventId = (int)ErrorCode.LogConsistency_CaughtException,
            Level = LogLevel.Error,
            Message = "{GrainId} exception caught at {Location}"
        )]
        private static partial void LogErrorCaughtException(ILogger logger, Exception exception, GrainId grainId, string location);

        [LoggerMessage(
            EventId = (int)ErrorCode.LogConsistency_UserCodeException,
            Level = LogLevel.Warning,
            Message = "{GrainId} exception caught in user code for {Callback}, called from {Location}"
        )]
        private static partial void LogWarningCaughtUserCodeException(ILogger logger, Exception exception, GrainId grainId, string callback, string location);
    }

}

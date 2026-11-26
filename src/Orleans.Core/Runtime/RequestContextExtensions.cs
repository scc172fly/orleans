using System;
using System.Collections.Generic;
using System.Diagnostics;
using Orleans.Diagnostics;
using Orleans.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 与 <see cref="RequestContext"/>合作的延长期。
    /// </summary>
    public static class RequestContextExtensions
    {
        /// <summary>
        /// 将指定的上下文数据导入当前 <see cref="RequestContext"/>,清除所有现有值。
        /// </summary>
        /// <param name="contextData">上下文数据。</param>
        public static void Import(Dictionary<string, object>? contextData)
        {
            var values = contextData switch
            {
                { Count: > 0 } => new Dictionary<string, object>(contextData),
                _ => null,
            };

            RequestContext.CallContextData.Value = new RequestContext.ContextProperties
            {
                Values = values,
            };
        }

        /// <summary>
        /// a 出口当前 <see cref="RequestContext"/>的副本。
        /// </summary>
        /// <param name="copier">复印机。</param>
        /// <returns>当前请求的背景副本 。</returns>
        public static Dictionary<string, object>? Export(DeepCopier copier)
        {
            var properties = RequestContext.CallContextData.Value;
            var values = properties.Values;

            var resultValues = (values != null && values.Count > 0)
                ? copier.Copy(values)
                : null;
            return resultValues;
        }

        internal static Guid GetReentrancyId(this Message message) => GetReentrancyId(message?.RequestContextData);

        internal static Guid GetReentrancyId(Dictionary<string, object>? contextData)
        {
            if (contextData is not { Count: > 0 }) return Guid.Empty;
            _ = contextData.TryGetValue(RequestContext.CALL_CHAIN_REENTRANCY_HEADER, out var reentrancyId);
            return reentrancyId is Guid guid ? guid : Guid.Empty;
        }

        /// <summary>
        /// 如果有请求的上下文数据,则从该请求中提取 ActivityContext 。
        /// </summary>
        internal static ActivityContext? TryGetActivityContext(this Dictionary<string, object>? requestContextData)
        {
            if (requestContextData is not { Count: > 0 })
            {
                return null;
            }

            string? traceParent = null;
            string? traceState = null;

            if (requestContextData.TryGetValue(OpenTelemetryHeaders.TraceParent, out var traceParentObj) && traceParentObj is string tp)
            {
                traceParent = tp;
            }

            if (requestContextData.TryGetValue(OpenTelemetryHeaders.TraceState, out var traceStateObj) && traceStateObj is string ts)
            {
                traceState = ts;
            }

            if (!string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var parentContext))
            {
                return parentContext;
            }

            return null;
        }
    }
}

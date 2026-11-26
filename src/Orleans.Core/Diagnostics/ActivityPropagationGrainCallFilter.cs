using System.Diagnostics;
using Orleans.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 帮助在呼叫链中传播活动相关性信息的 grain 调用过滤器。
    /// </summary>
    internal abstract class ActivityPropagationGrainCallFilter
    {
        internal const string RpcSystem = "orleans";
        internal const string OrleansNamespacePrefix = "Orleans";

        protected static ActivitySource GetActivitySource(IGrainCallContext context)
        {
            var interfaceType = context.Request.GetInterfaceType();
            var interfaceTypeName = interfaceType.Name;

            switch (interfaceTypeName)
            {
                // 内存存储用 grains 实施
                case "IMemoryStorageGrain":
                    return ActivitySources.StorageGrainSource;

                // 此项扩展适用于明确的移徙/停止行动电话
                case "IGrainManagementExtension":
                // 目标是接受移徙批次
                case "IActivationMigrationManagerSystemTarget":
                    return ActivitySources.LifecycleGrainSource;

                // 这些扩展号是用于同步流订阅的
                case "IAsyncEnumerableGrainExtension":
                    return ActivitySources.ApplicationGrainSource;

                default:
                    return interfaceType.Namespace?.StartsWith(OrleansNamespacePrefix, StringComparison.Ordinal) == true
                        ? ActivitySources.RuntimeGrainSource
                        : ActivitySources.ApplicationGrainSource;
            }
        }

        protected static void GetRequestContextValue(object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues)
        {
            fieldValues = default;
            fieldValue = RequestContext.Get(fieldName) as string;
        }

        protected static async Task Process(IGrainCallContext context, Activity activity)
        {
            if (activity is not null)
            {
                // https://opentelemetry.io/docs/specs/semconv/rpc/的 RPC 属性
                activity.SetTag(ActivityTagKeys.RpcSystem, RpcSystem);
                activity.SetTag(ActivityTagKeys.RpcService, context.InterfaceName);
                activity.SetTag(ActivityTagKeys.RpcMethod, $"{context.InterfaceName}/{context.MethodName}");

                if (activity.IsAllDataRequested)
                {
                    // 自定义属性
                    activity.SetTag(ActivityTagKeys.RpcOrleansTargetId, context.TargetId.ToString());
                    if (context.SourceId is GrainId sourceId)
                    {
                        activity.SetTag(ActivityTagKeys.RpcOrleansSourceId, sourceId.ToString());
                    }
                }
            }

            try
            {
                await context.Invoke();
            }
            catch (Exception e)
            {
                if (activity is not null && activity.IsAllDataRequested)
                {
                    activity.SetStatus(ActivityStatusCode.Error);

                    // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/exceptions.md的例外属性
                    activity.SetTag(ActivityTagKeys.ExceptionType, e.GetType().FullName);
                    activity.SetTag(ActivityTagKeys.ExceptionMessage, e.Message);

                    // 请注意, "exception.stacktrace" 是全部例外细节,而不仅仅是 StackTrace 财产。
                    // 见 https://opentelemetry.io/docs/specs/semconv/attributes-registry/exception/
                    // 和 https://github.com/open-telemetry/opentelemetry-specification/pull/697#discussion_r453662519
                    activity.SetTag(ActivityTagKeys.ExceptionStacktrace, e.ToString());
                    activity.SetTag(ActivityTagKeys.ExceptionEscaped, true);
                }

                throw;
            }
            finally
            {
                activity?.Stop();
            }
        }
    }

    /// <summary>
    /// 方案向外出请求分发背景资料。
    /// </summary>
    internal class ActivityPropagationOutgoingGrainCallFilter : ActivityPropagationGrainCallFilter, IOutgoingGrainCallFilter
    {
        private readonly DistributedContextPropagator _propagator;

        /// <summary>
        /// 初始化 <see cref="ActivityPropagationOutgoingGrainCallFilter"/> 类的新实例。
        /// </summary>
        /// <param name="propagator">上下文宣传器。</param>
        public ActivityPropagationOutgoingGrainCallFilter(DistributedContextPropagator propagator)
        {
            _propagator = propagator;
        }

        /// <inheritdoc />
        public Task Invoke(IOutgoingGrainCallContext context)
        {
            var source = GetActivitySource(context);
            var activity = source.StartActivity(context.Request.GetActivityName(), ActivityKind.Client);

            if (activity is null)
            {
                return context.Invoke();
            }

            _propagator.Inject(activity, null, static (carrier, key, value) => RequestContext.Set(key, value));
            return Process(context, activity);
        }
    }

    /// <summary>
    /// 分发收到的请求的背景资料。
    /// </summary>
    internal class ActivityPropagationIncomingGrainCallFilter : ActivityPropagationGrainCallFilter, IIncomingGrainCallFilter
    {
        private readonly DistributedContextPropagator _propagator;

        /// <summary>
        /// 初始化 <see cref="ActivityPropagationIncomingGrainCallFilter"/> 类的新实例。
        /// </summary>
        /// <param name="propagator">上下文宣传器。</param>
        public ActivityPropagationIncomingGrainCallFilter(DistributedContextPropagator propagator)
        {
            _propagator = propagator;
        }

        /// <inheritdoc />
        public Task Invoke(IIncomingGrainCallContext context)
        {
            Activity? activity = default;
            _propagator.ExtractTraceIdAndState(null, GetRequestContextValue, out var traceParent, out var traceState);

            var source = GetActivitySource(context);
            if (!string.IsNullOrEmpty(traceParent))
            {
                if (ActivityContext.TryParse(traceParent, traceState, isRemote: true, out ActivityContext parentContext))
                {
                    // traceParent 是 W3CId 的 W3CId
                    activity = source.CreateActivity(context.Request.GetActivityName(), ActivityKind.Server, parentContext);
                }
                else
                {
                    // 最有可能的是 traceParent 使用 ActivityIdFormat.Hierarchical
                    activity = source.CreateActivity(context.Request.GetActivityName(), ActivityKind.Server, traceParent);
                }

                if (activity is not null)
                {
                    if (!string.IsNullOrEmpty(traceState))
                    {
                        activity.TraceStateString = traceState;
                    }

                    var baggage = _propagator.ExtractBaggage(null, GetRequestContextValue);

                    if (baggage is not null)
                    {
                        foreach (var baggageItem in baggage)
                        {
                            activity.AddBaggage(baggageItem.Key, baggageItem.Value);
                        }
                    }
                }
            }
            else
            {
                activity = source.CreateActivity(context.Request.GetActivityName(), ActivityKind.Server);
            }

            if (activity is null)
            {
                return context.Invoke();
            }

            activity.Start();
            return Process(context, activity);
        }
    }
}

using System.Diagnostics;
using Orleans.Diagnostics;

namespace Orleans.Runtime
{
    /// <summary>
    /// 帮助沿调用链传播 Activity 关联信息的 Grain 调用筛选器。
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
                // 内存存储使用 Grain 实现。
                case "IMemoryStorageGrain":
                    return ActivitySources.StorageGrainSource;

                // 此扩展用于显式迁移或停用调用。
                case "IGrainManagementExtension":
                // 此目标用于接收批量迁移。
                case "IActivationMigrationManagerSystemTarget":
                    return ActivitySources.LifecycleGrainSource;

                // 这些扩展用于异步流订阅。
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
                // RPC 属性参见 https://opentelemetry.io/docs/specs/semconv/rpc/
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

                    // 异常属性参见 https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/exceptions.md
                    activity.SetTag(ActivityTagKeys.ExceptionType, e.GetType().FullName);
                    activity.SetTag(ActivityTagKeys.ExceptionMessage, e.Message);

                    // 注意：“exception.stacktrace” 包含完整的异常详情，而不只是 StackTrace 属性。
                    // 参见 https://opentelemetry.io/docs/specs/semconv/attributes-registry/exception/
                    // 以及 https://github.com/open-telemetry/opentelemetry-specification/pull/697#discussion_r453662519
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
    /// 将分布式上下文信息传播到传出请求。
    /// </summary>
    internal class ActivityPropagationOutgoingGrainCallFilter : ActivityPropagationGrainCallFilter, IOutgoingGrainCallFilter
    {
        private readonly DistributedContextPropagator _propagator;

        /// <summary>
        /// 初始化 <see cref="ActivityPropagationOutgoingGrainCallFilter"/> 类的新实例。
        /// </summary>
        /// <param name="propagator">上下文传播器。</param>
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
    /// 从传入请求填充分布式上下文信息。
    /// </summary>
    internal class ActivityPropagationIncomingGrainCallFilter : ActivityPropagationGrainCallFilter, IIncomingGrainCallFilter
    {
        private readonly DistributedContextPropagator _propagator;

        /// <summary>
        /// 初始化 <see cref="ActivityPropagationIncomingGrainCallFilter"/> 类的新实例。
        /// </summary>
        /// <param name="propagator">上下文传播器。</param>
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
                    // traceParent 是 W3CId。
                    activity = source.CreateActivity(context.Request.GetActivityName(), ActivityKind.Server, parentContext);
                }
                else
                {
                    // traceParent 很可能使用 ActivityIdFormat.Hierarchical。
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

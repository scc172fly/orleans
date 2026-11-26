using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// 向 grain提出请求。
    /// </summary>
    internal sealed class GrainMethodInvoker : IIncomingGrainCallContext
    {
        private readonly Message message;
        private readonly IInvokable request;
        private readonly List<IIncomingGrainCallFilter> filters;
        private readonly InterfaceToImplementationMappingCache interfaceToImplementationMapping;
        private readonly DeepCopier<Response> responseCopier;
        private readonly IGrainContext grainContext;
        private int stage;

        /// <summary>
        /// 初始化 <see cref="GrainMethodInvoker"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="grainContext">下 grain.</param>
        /// <param name="request">请求。</param>
        /// <param name="filters">请求拦截器</param>
        /// <param name="interfaceToImplementationMapping">执行地图。</param>
        /// <param name="responseCopier">响应复印机。</param>
        public GrainMethodInvoker(
            Message message,
            IGrainContext grainContext,
            IInvokable request,
            List<IIncomingGrainCallFilter> filters,
            InterfaceToImplementationMappingCache interfaceToImplementationMapping,
            DeepCopier<Response> responseCopier)
        {
            this.message = message;
            this.request = request;
            this.grainContext = grainContext;
            this.filters = filters;
            this.interfaceToImplementationMapping = interfaceToImplementationMapping;
            this.responseCopier = responseCopier;
        }

        public IInvokable Request => request;

        public object Grain => grainContext.GrainInstance!;

        public MethodInfo InterfaceMethod => request.GetMethod();

        public MethodInfo ImplementationMethod => GetMethodEntry().ImplementationMethod;

        public object? Result
        {
            get => Response switch
            {
                { Exception: null } response => response.Result,
                _ => null
            };
            set => Response = Response.FromResult(value);
        }

        public Response? Response { get; set; }

        public GrainId? SourceId => message.SendingGrain is { IsDefault: false } source ? source : null;

        public IGrainContext TargetContext => grainContext;

        public GrainId TargetId => grainContext.GrainId;

        public GrainInterfaceType InterfaceType => message.InterfaceType;

        public string InterfaceName => request.GetInterfaceName();

        public string MethodName => request.GetMethodName();

        public async Task Invoke()
        {
            try
            {
                // 执行输油管中的每个阶段,每次连续呼叫使用这种方法时,都将采用下一阶段。
                // 未执行的阶段( 例如, 因为用户未指定拦截器) 被跳过 。
                var numFilters = filters.Count;
                if (stage < numFilters)
                {
                    // 呼叫每个指定的拦截器
                    var systemWideFilter = this.filters[stage];
                    stage++;
                    await systemWideFilter.Invoke(this);

                    // 如果回复为无效, 某些过滤器没有继续呼叫链
                    if (this.Response is null)
                    {
                        ThrowBrokenCallFilterChain(systemWideFilter.GetType().Name);
                    }

                    return;
                }

                if (stage == numFilters)
                {
                    stage++;

                    // Grain- 级援引者,如果存在的话。
                    if (this.Grain is IIncomingGrainCallFilter grainClassLevelFilter)
                    {
                        await grainClassLevelFilter.Invoke(this);

                        // 如果回复为无效, 某些过滤器没有继续呼叫链
                        if (this.Response is null)
                        {
                            ThrowBrokenCallFilterChain(this.Grain.GetType().Name);
                        }
                        return;
                    }
                }

                if (stage == numFilters + 1)
                {
                    // 最后打电话给根级的援引者。
                    stage++;
                    this.Response = await request.Invoke();

                    // 提出其他过滤器的例外。
                    if (this.Response.Exception is { } exception)
                    {
                        ExceptionDispatchInfo.Capture(exception).Throw();
                    }

                    // 当地请求以参考方式通过,其答复也以参考方式通过。
                    if (!message.IsLocalOnly)
                    {
                        this.Response = this.responseCopier.Copy(this.Response);
                    }

                    return;
                }
            }
            finally
            {
                stage--;
            }

            // 如果该方法被调用的次数超过预期,则无效。
            ThrowInvalidCall();
        }

        private static void ThrowInvalidCall()
        {
            throw new InvalidOperationException(
                $"{nameof(GrainMethodInvoker)}.{nameof(Invoke)}() received an invalid call.");
        }

        private static void ThrowBrokenCallFilterChain(string filterName)
        {
            throw new InvalidOperationException($"{nameof(GrainMethodInvoker)}.{nameof(Invoke)}() invoked a broken filter: {filterName}.");
        }


        private (MethodInfo ImplementationMethod, MethodInfo InterfaceMethod) GetMethodEntry()
        {
            var interfaceType = this.request.GetInterfaceType();
            var implementationType = this.request.GetTarget()!.GetType();

            // 获取或创建此对象的执行映射。
            var implementationMap = interfaceToImplementationMapping.GetOrCreate(
                implementationType,
                interfaceType);

            // 获取被引用的方法的方法信息 。
            var method = request.GetMethod();
            if (method.IsConstructedGenericMethod)
            {
                if (implementationMap.TryGetValue(method.GetGenericMethodDefinition(), out var entry))
                {
                    return entry.GetConstructedGenericMethod(method);
                }
            }
            else if (implementationMap.TryGetValue(method, out var entry))
            {
                return (entry.ImplementationMethod, entry.InterfaceMethod);
            }

            Debug.Assert(false, "Method entry not found");
            return default;
        }
    }
}

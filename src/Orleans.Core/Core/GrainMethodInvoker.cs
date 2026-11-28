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
    /// 在 Grain 上调用请求。
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
        /// <param name="message">消息。</param>
        /// <param name="grainContext">Grain。</param>
        /// <param name="request">请求。</param>
        /// <param name="filters">调用拦截器。</param>
        /// <param name="interfaceToImplementationMapping">实现映射。</param>
        /// <param name="responseCopier">响应复制器。</param>
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
                // 执行管道中的各个阶段。每次后续调用此方法都会执行下一个阶段。
                // 跳过没有实现的阶段（例如用户未指定拦截器）。
                var numFilters = filters.Count;
                if (stage < numFilters)
                {
                    // 调用每个指定的拦截器。
                    var systemWideFilter = this.filters[stage];
                    stage++;
                    await systemWideFilter.Invoke(this);

                    // 如果 Response 为 null，则说明某个筛选器未继续执行调用链。
                    if (this.Response is null)
                    {
                        ThrowBrokenCallFilterChain(systemWideFilter.GetType().Name);
                    }

                    return;
                }

                if (stage == numFilters)
                {
                    stage++;

                    // Grain 级调用器（如果存在）。
                    if (this.Grain is IIncomingGrainCallFilter grainClassLevelFilter)
                    {
                        await grainClassLevelFilter.Invoke(this);

                        // 如果 Response 为 null，则说明某个筛选器未继续执行调用链。
                        if (this.Response is null)
                        {
                            ThrowBrokenCallFilterChain(this.Grain.GetType().Name);
                        }
                        return;
                    }
                }

                if (stage == numFilters + 1)
                {
                    // 最后调用根级调用器。
                    stage++;
                    this.Response = await request.Invoke();

                    // 将异常传播给其他筛选器。
                    if (this.Response.Exception is { } exception)
                    {
                        ExceptionDispatchInfo.Capture(exception).Throw();
                    }

                    // 本地请求按引用传递，其响应也是如此。
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

            // 调用此方法的次数超过预期次数属于无效操作。
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

            // 获取或创建此对象的实现映射。
            var implementationMap = interfaceToImplementationMapping.GetOrCreate(
                implementationType,
                interfaceType);

            // 获取正在调用的方法的方法元数据。
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

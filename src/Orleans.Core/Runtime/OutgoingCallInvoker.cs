using System;
using System.Reflection;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// 在 Grain 引用上调用请求。
    /// </summary>
    internal sealed class OutgoingCallInvoker<TResult> : IOutgoingGrainCallContext
    {
        private readonly IInvokable request;
        private readonly InvokeMethodOptions options;
        private readonly Action<GrainReference, IResponseCompletionSource, IInvokable, InvokeMethodOptions> sendRequest;
        private readonly IOutgoingGrainCallFilter[] filters;
        private readonly int stages;
        private readonly GrainReference grainReference;
        private readonly IOutgoingGrainCallFilter? requestFilter;
        private int stage;

        /// <summary>
        /// 初始化 <see cref="OutgoingCallInvoker{TResult}"/> 类的新实例。
        /// </summary>
        /// <param name="grain">Grain 引用。</param>
        /// <param name="request">请求。</param>
        /// <param name="options"></param>
        /// <param name="sendRequest"></param>
        /// <param name="filters">调用拦截器。</param>
        public OutgoingCallInvoker(
            GrainReference grain,
            IInvokable request,
            InvokeMethodOptions options,
            Action<GrainReference, IResponseCompletionSource, IInvokable, InvokeMethodOptions> sendRequest,
            IOutgoingGrainCallFilter[] filters)
        {
            this.request = request;
            this.options = options;
            this.sendRequest = sendRequest;
            this.grainReference = grain;
            this.filters = filters;
            this.stages = filters.Length;
            SourceContext = RuntimeContext.Current;

            if (request is IOutgoingGrainCallFilter requestFilter)
            {
                this.requestFilter = requestFilter;
                ++this.stages;
            }
        }

        public IInvokable Request => this.request;

        public object Grain => this.grainReference;

        public MethodInfo InterfaceMethod => request.GetMethod();

        public object? Result { get => TypedResult; set => TypedResult = (TResult?)value; }

        public Response? Response { get; set; }

        public TResult? TypedResult { get => Response!.GetResult<TResult>(); set => Response = Response.FromResult(value); }

        public IGrainContext? SourceContext { get; }

        public GrainId? SourceId => SourceContext?.GrainId;

        public GrainId TargetId => grainReference.GrainId;

        public GrainInterfaceType InterfaceType => grainReference.InterfaceType;

        public string InterfaceName => request.GetInterfaceName();

        public string MethodName => request.GetMethodName();

        public async Task Invoke()
        {
            try
            {
                // 执行管道中的各个阶段。每次后续调用此方法都会执行下一个阶段。
                // 跳过没有实现的阶段（例如用户未指定拦截器）。
                if (stage < this.filters.Length)
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
                else if (stage < this.stages)
                {
                    stage++;
                    await this.requestFilter!.Invoke(this);

                    // 如果 Response 为 null，则说明某个筛选器未继续执行调用链。
                    if (this.Response is null)
                    {
                        ThrowBrokenCallFilterChain(this.requestFilter.GetType().Name);
                    }

                    return;
                }
                else if (stage == this.stages)
                {
                    // 最后调用根级调用器。
                    stage++;
                    var responseCompletionSource = ResponseCompletionSourcePool.Get();
                    this.sendRequest(this.grainReference, responseCompletionSource, this.request, this.options);
                    this.Response = await responseCompletionSource.AsValueTask().ConfigureAwait(false);

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

        private void ThrowInvalidCall()
        {
            throw new InvalidOperationException($"{typeof(OutgoingCallInvoker<TResult>)}.{nameof(Invoke)}() received an invalid call.");
        }

        private void ThrowBrokenCallFilterChain(string filterName)
        {
            throw new InvalidOperationException($"{typeof(OutgoingCallInvoker<TResult>)}.{nameof(Invoke)}() invoked a broken filter: {filterName}.");
        }
    }
}

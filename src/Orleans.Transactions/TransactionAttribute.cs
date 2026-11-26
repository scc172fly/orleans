using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Orleans.Transactions;

namespace Orleans
{
    /// <summary>
    /// TransactionAttribute 属性用于标记开始和加入交易的方法。
    /// </summary>
    [InvokableCustomInitializer("SetTransactionOptions")]
    [InvokableBaseType(typeof(GrainReference), typeof(ValueTask), typeof(TransactionRequest))]
    [InvokableBaseType(typeof(GrainReference), typeof(ValueTask<>), typeof(TransactionRequest<>))]
    [InvokableBaseType(typeof(GrainReference), typeof(Task), typeof(TransactionTaskRequest))]
    [InvokableBaseType(typeof(GrainReference), typeof(Task<>), typeof(TransactionTaskRequest<>))]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TransactionAttribute : Attribute
    {
        /// <summary>
        /// 初始化 <see cref="TransactionAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="requirement">配给方法要求的交易行为。</param>
        public TransactionAttribute(TransactionOption requirement)
        {
            Requirement = requirement;
        }

        /// <summary>
        /// 初始化 <see cref="TransactionAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="alias">指定方法所要求的交易行为的别名。</param>
        public TransactionAttribute(TransactionOptionAlias alias)
        {
            Requirement = (TransactionOption)(int)alias;
        }

        /// <summary>
        /// 获得配给方法所要求的交易行为。
        /// </summary>
        public TransactionOption Requirement { get; }

        /// <summary>
        /// 获取或设定一个数值,表明交易是否允许只读操作。
        /// </summary>
        [Obsolete("Use [ReadOnly] attribute instead.")]
        public bool ReadOnly { get; set; }
    }

    /// <summary>
    /// UseExclusiveLock 属性用于标示应当获得的交易方法
    /// 即使是用于阅读操作的独家锁,也防止经常发生争议激烈的锁升级冲突。
    /// </summary>
    /// <example>
    /// <code>
    /// [UseExclusiveLock]
    /// [Transaction(TransactionOption.CreateOrJoin)]
    /// Task&lt;uint&gt; GetBalance();
    /// </code>
    /// </example>
    [InvokableCustomInitializer("SetExclusiveLock", true)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UseExclusiveLockAttribute : Attribute
    {
    }

    /// <summary>
    /// 指定方法调用或委托如何与环境交易发生相互作用。
    /// </summary>
    public enum TransactionOption
    {
        /// <summary>
        /// 执行 grain 电话而不传播环境交易。代表执行拒绝现有的环境交易 。
        /// </summary>
        Suppress,

        /// <summary>
        /// 存在环境交易时加入该环境交易;否则,创建交易。
        /// </summary>
        CreateOrJoin,

        /// <summary>
        /// 创建一项交易,独立于任何环境交易。
        /// </summary>
        Create,

        /// <summary>
        /// 加入环境交易,要求存在。
        /// </summary>
        Join,

        /// <summary>
        /// 当存在环境交易时,则进行环境交易,并且在没有其他交易的情况下执行。
        /// </summary>
        Supported,

        /// <summary>
        /// 执行无交易,拒绝环境交易中发出的电话。
        /// </summary>
        NotAllowed
    }

    /// <summary>
    /// 提供 <see cref="TransactionOption"/> 值的兼容性别名。
    /// </summary>
    public enum TransactionOptionAlias
    {
        /// <summary>
        /// 地图到 <see cref="TransactionOption.Supported"/>。
        /// </summary>
        Suppress = TransactionOption.Supported,

        /// <summary>
        /// 地图到 <see cref="TransactionOption.CreateOrJoin"/>。
        /// </summary>
        Required = TransactionOption.CreateOrJoin,

        /// <summary>
        /// 地图到 <see cref="TransactionOption.Create"/>。
        /// </summary>
        RequiresNew = TransactionOption.Create,

        /// <summary>
        /// 地图到 <see cref="TransactionOption.Join"/>。
        /// </summary>
        Mandatory = TransactionOption.Join,

        /// <summary>
        /// 地图到 <see cref="TransactionOption.NotAllowed"/>。
        /// </summary>
        Never = TransactionOption.NotAllowed,
    }

    /// <summary>
    /// 为生成 grain 请求查询者提供交易传播和解决。
    /// </summary>
    [GenerateSerializer]
    public abstract class TransactionRequestBase : RequestBase, IOutgoingGrainCallFilter, IOnDeserialized
    {
        [NonSerialized]
        private Serializer<OrleansTransactionAbortedException> _serializer;

        [NonSerialized]
        private ITransactionAgent? _transactionAgent;

        private ITransactionAgent TransactionAgent => _transactionAgent ?? throw new OrleansTransactionsDisabledException();

        /// <summary>
        /// 获取或设置请求的交易行为。
        /// </summary>
        [Id(0)]
        public TransactionOption TransactionOption { get; set; }

        /// <summary>
        /// 获取或设置随请求传播的交易信息。
        /// </summary>
        [Id(1)]
        public TransactionInfo? TransactionInfo { get; set; }

        /// <summary>
        /// 获取或设定一个值,以显示交易状态是否读取了独家锁。
        /// </summary>
        [Id(2)]
        public bool UseExclusiveLock { get; set; }

        /// <summary>
        /// 初始化 <see cref="TransactionRequestBase"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">交易中止例外的序列器。</param>
        /// <param name="serviceProvider">用于解决交易服务的服务提供者。</param>
        [GeneratedActivatorConstructor]
        protected TransactionRequestBase(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider)
        {
            _serializer = exceptionSerializer;

            // 可能无效, 例如对外部客户端。 如果在援引时无效, 我们将丢弃它 。
            _transactionAgent = serviceProvider.GetService<ITransactionAgent>();
        }

        /// <summary>
        /// 获得一个数值,显示请求是否在环境交易之外执行。
        /// </summary>
        public bool IsAmbientTransactionSuppressed => TransactionOption switch
        {
            TransactionOption.Create => true,
            TransactionOption.Suppress => true,
            _ => false
        };

        /// <summary>
        /// 获得一个值,显示请求是否需要交易环境。
        /// </summary>
        public bool IsTransactionRequired => TransactionOption switch
        {
            TransactionOption.Create => true,
            TransactionOption.CreateOrJoin => true,
            TransactionOption.Join => true,
            _ => false
        };

        /// <summary>
        /// 使用兼容性别名设置交易行为。
        /// </summary>
        /// <param name="txOption">交易选项别名。</param>
        protected void SetTransactionOptions(TransactionOptionAlias txOption) => SetTransactionOptions((TransactionOption)txOption);

        /// <summary>
        /// 设置请求的交易行为。
        /// </summary>
        /// <param name="txOption">交易选项。</param>
        protected void SetTransactionOptions(TransactionOption txOption)
        {
            this.TransactionOption = txOption;
        }

        /// <summary>
        /// 设定交易状态是否读取获取独家锁 。
        /// </summary>
        /// <param name="value">
        /// <see langword="true"/> 获取专用锁,以作以下用途;否则, <see langword="false"/>。
        /// </param>
        protected void SetExclusiveLock(bool value)
        {
            this.UseExclusiveLock = value;
        }

        async Task IOutgoingGrainCallFilter.Invoke(IOutgoingGrainCallContext context)
        {
            var transactionInfo = SetTransactionInfo();
            try
            {
                await context.Invoke();
            }
            finally
            {
                if (context.Response is TransactionResponse txResponse)
                {
                    var returnedTransactionInfo = txResponse.TransactionInfo;

                    if (transactionInfo is { } && returnedTransactionInfo is { })
                    {
                        transactionInfo.Join(returnedTransactionInfo);
                    }

                    if (txResponse.GetException() is { } exception)
                    {
                        ExceptionDispatchInfo.Throw(exception);
                    }
                }
            }
        }

        private TransactionInfo? SetTransactionInfo()
        {
            // 如果交易操作需要新的交易, 请清除交易信息 。
            var transactionInfo = TransactionContext.GetTransactionInfo();

            // 强制加入交易电话
            if (TransactionOption == TransactionOption.Join && transactionInfo == null)
            {
                throw new NotSupportedException("Call cannot be made outside of a transaction.");
            }

            // 不允许强制执行交易电话
            if (TransactionOption == TransactionOption.NotAllowed && transactionInfo != null)
            {
                throw new NotSupportedException("Call cannot be made within a transaction.");
            }

            // 如果创建交易或交易被抑制, 将清除交易上下文
            if (TransactionOption is TransactionOption.Create or TransactionOption.Suppress)
            {
                transactionInfo = null;
            }

            if (transactionInfo == null)
            {
                // 如果我们离开交易背景 确保它被清除 从请求的背景。
                TransactionContext.Clear();
            }
            else
            {
                this.TransactionInfo = transactionInfo.Fork();
            }

            return transactionInfo;
        }

        /// <inheritdoc/>
        public override async ValueTask<Response> Invoke()
        {
            Response? response;
            var transactionInfo = this.TransactionInfo;
            bool startedNewTransaction = false;
            try
            {
                if (IsTransactionRequired && transactionInfo == null)
                {
                    // TODO: 这应该是可配置参数
                    var transactionTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(10);

                    // 开始新交易
                    var isReadOnly = this.Options.HasFlag(InvokeMethodOptions.ReadOnly);
                    transactionInfo = await TransactionAgent.StartTransaction(isReadOnly, transactionTimeout);
                    startedNewTransaction = true;
                }

                // 如果属性要求应用此标记
                if (this.UseExclusiveLock && transactionInfo != null)
                {
                    transactionInfo.UseExclusiveLock = true;
                }

                TransactionContext.SetTransactionInfo(transactionInfo);
                response = await BaseInvoke();
            }
            catch (Exception exception)
            {
                response = Response.FromException(exception);
            }
            finally
            {
                TransactionContext.Clear();
            }

            if (transactionInfo is null)
            {
                return response;
            }

            try
            {
                transactionInfo.ReconcilePending();

                if (response.Exception is { } invokeException)
                {
                    // 中止记录理由, 如果尚未设置的话 。
                    transactionInfo.RecordException(invokeException, _serializer);
                }

                OrleansTransactionException? transactionException = transactionInfo.MustAbort(_serializer);

                // 请求启动了交易 所以我们在返回前要承诺
                // 或者如果必须中止,请告诉参与者它已经中止
                if (startedNewTransaction)
                {
                    if (transactionException is not null || transactionInfo.TryToCommit is false)
                    {
                        await TransactionAgent.Abort(transactionInfo);
                    }
                    else
                    {
                        var (status, exception) = await TransactionAgent.Resolve(transactionInfo);
                        if (status != TransactionalStatus.Ok)
                        {
                            transactionException = status.ConvertToUserException(transactionInfo.Id, exception);
                        }
                    }
                }

                if (transactionException != null)
                {
                    return TransactionResponse.Create(Response.FromException(transactionException), transactionInfo);
                }

                var result = TransactionResponse.Create(response, transactionInfo);
                response = null; // 返回的包装纸现在拥有要求响应。
                return result;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// 启动生成的请求执行。
        /// </summary>
        /// <returns>代表呼唤结果的答复。</returns>
        protected abstract ValueTask<Response> BaseInvoke();

        /// <inheritdoc/>
        public override void Dispose()
        {
            TransactionInfo = null;
        }

        void IOnDeserialized.OnDeserialized(DeserializationContext context)
        {
            _serializer = context.ServiceProvider.GetRequiredService<Serializer<OrleansTransactionAbortedException>>();
            _transactionAgent = context.ServiceProvider.GetRequiredService<ITransactionAgent>();
        }
    }

    /// <summary>
    /// 将答复与援引产生的交易信息包装在一起。
    /// </summary>
    [GenerateSerializer]
    public sealed class TransactionResponse : Response
    {
        [Id(0)]
        private Response _response = null!;

        /// <summary>
        /// 获取或设置通过援引返回的交易信息。
        /// </summary>
        [Id(1)]
        public TransactionInfo? TransactionInfo { get; set; }

        /// <summary>
        /// 创建交易响应,以包绕调用响应。
        /// </summary>
        /// <param name="response">所有权转移到返回的包装纸上的呼唤反应。</param>
        /// <param name="transactionInfo">随回复返回的交易信息。</param>
        /// <returns>交易反应, 打电话者处置 释放包装反应。</returns>
        public static TransactionResponse Create(Response response, TransactionInfo transactionInfo)
        {
            return new TransactionResponse
            {
                _response = response,
                TransactionInfo = transactionInfo
            };
        }

        /// <summary>
        /// 得到包装的求救反应
        /// </summary>
        public Response InnerResponse => _response;

        /// <summary>
        /// 获取或设置包装响应结果 。
        /// </summary>
        public override object? Result
        {
            get
            {
                if (_response.Exception is { } exception)
                {
                    ExceptionDispatchInfo.Capture(exception).Throw();
                }

                return _response.Result;
            }

            set => _response.Result = value;
        }

        /// <summary>
        /// 获取 <see langword="null"/> ,以便包装纸能够作为成功响应交付,或设置
        /// 包装响应的例外。
        /// </summary>
        public override Exception? Exception
        {
            get
            {
                // 在此禁止任何例外, 允许 ResponseCompletionSource 在答复中填写完整, 而不是例外 。
                // 这给了 TransactionRequestBase 检查此实例并首先检索 TransactionInfo 属性的机会。
                // 之后,它将用 GetException 来获取和丢弃豁免。
                return null;
            }

            set => _response.Exception = value;
        }

        /// <summary>
        /// 得到例外 从包装回应。
        /// </summary>
        /// <returns>包装例外,或如果成功完成调用,则 <see langword="null"/> 。</returns>
        public Exception? GetException() => _response.Exception;

        /// <inheritdoc/>
        public override string ToString() => _response?.ToString() ?? "[null]";

        /// <inheritdoc/>
        public override void Dispose()
        {
            TransactionInfo = null;
            _response.Dispose();
        }

        /// <inheritdoc/>
        [return: MaybeNull]
        public override T GetResult<T>() => _response.GetResult<T>();
    }

    /// <summary>
    /// 返回 <see cref="ValueTask"/>的生成交易请求请求请求请求请求请求人的基准类型。
    /// </summary>
    [SerializerTransparent]
    public abstract class TransactionRequest : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionRequest"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">交易中止例外的序列器。</param>
        /// <param name="serviceProvider">用于解决交易服务的服务提供者。</param>
        protected TransactionRequest(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider) : base(exceptionSerializer, serviceProvider)
        {
        }

        /// <inheritdoc/>
        protected sealed override ValueTask<Response> BaseInvoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
#pragma warning disable CA1849 // IsCompleted proves this consumes the ValueTask result without blocking, including failure or cancellation.
                    resultTask.GetAwaiter().GetResult();
#pragma warning restore CA1849
                    return new ValueTask<Response>(Response.Completed);
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(ValueTask resultTask)
        {
            try
            {
                await resultTask;
                return Response.Completed;
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        // 生成
        /// <summary>
        /// 启动生成的方法实施。
        /// </summary>
        /// <returns>代表召唤者的任务</returns>
        protected abstract ValueTask InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="ValueTask{TResult}"/>的生成交易请求请求请求请求请求请求人的基准类型。
    /// </summary>
    /// <typeparam name="TResult">调用结果类型。</typeparam>
    [SerializerTransparent]
    public abstract class TransactionRequest<TResult> : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionRequest{TResult}"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">交易中止例外的序列器。</param>
        /// <param name="serviceProvider">用于解决交易服务的服务提供者。</param>
        protected TransactionRequest(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider) : base(exceptionSerializer, serviceProvider)
        {
        }

        /// <inheritdoc/>
        protected sealed override ValueTask<Response> BaseInvoke()
        {
            try
            {
                var resultTask = InvokeInner();
                if (resultTask.IsCompleted)
                {
                    return new ValueTask<Response>(Response.FromResult(resultTask.Result));
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(ValueTask<TResult> resultTask)
        {
            try
            {
                var result = await resultTask;
                return Response.FromResult(result);
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        // 生成
        /// <summary>
        /// 启动生成的方法实施。
        /// </summary>
        /// <returns>含有调用结果的任务。</returns>
        protected abstract ValueTask<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="Task{TResult}"/>的生成交易请求请求请求请求请求请求人的基准类型。
    /// </summary>
    /// <typeparam name="TResult">调用结果类型。</typeparam>
    [SerializerTransparent]
    public abstract class TransactionTaskRequest<TResult> : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionTaskRequest{TResult}"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">交易中止例外的序列器。</param>
        /// <param name="serviceProvider">用于解决交易服务的服务提供者。</param>
        protected TransactionTaskRequest(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider) : base(exceptionSerializer, serviceProvider)
        {
        }

        /// <inheritdoc/>
        protected sealed override ValueTask<Response> BaseInvoke()
        {
            try
            {
                var resultTask = InvokeInner();
                var status = resultTask.Status;
                if (resultTask.IsCompleted)
                {
                    return new ValueTask<Response>(Response.FromResult(resultTask.GetAwaiter().GetResult()));
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(Task<TResult> resultTask)
        {
            try
            {
                var result = await resultTask;
                return Response.FromResult(result);
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        // 生成
        /// <summary>
        /// 启动生成的方法实施。
        /// </summary>
        /// <returns>含有调用结果的任务。</returns>
        protected abstract Task<TResult> InvokeInner();
    }

    /// <summary>
    /// 返回 <see cref="Task"/>的生成交易请求请求请求请求请求请求人的基准类型。
    /// </summary>
    [SerializerTransparent]
    public abstract class TransactionTaskRequest : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionTaskRequest"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">交易中止例外的序列器。</param>
        /// <param name="serviceProvider">用于解决交易服务的服务提供者。</param>
        protected TransactionTaskRequest(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider) : base(exceptionSerializer, serviceProvider)
        {
        }

        /// <inheritdoc/>
        protected sealed override ValueTask<Response> BaseInvoke()
        {
            try
            {
                var resultTask = InvokeInner();
                var status = resultTask.Status;
                if (resultTask.IsCompleted)
                {
#pragma warning disable CA1849 // IsCompleted proves this observes the Task outcome without blocking, including failure or cancellation.
                    resultTask.GetAwaiter().GetResult();
#pragma warning restore CA1849
                    return new ValueTask<Response>(Response.Completed);
                }

                return CompleteInvokeAsync(resultTask);
            }
            catch (Exception exception)
            {
                return new ValueTask<Response>(Response.FromException(exception));
            }
        }

        private static async ValueTask<Response> CompleteInvokeAsync(Task resultTask)
        {
            try
            {
                await resultTask;
                return Response.Completed;
            }
            catch (Exception exception)
            {
                return Response.FromException(exception);
            }
        }

        // 生成
        /// <summary>
        /// 启动生成的方法实施。
        /// </summary>
        /// <returns>代表召唤者的任务</returns>
        protected abstract Task InvokeInner();
    }
}

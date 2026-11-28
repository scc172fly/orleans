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
    /// TransactionAttribute 特性用于标记启动及加入事务的方法。
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
        /// <param name="requirement">标记此特性的方法所要求的事务行为。</param>
        public TransactionAttribute(TransactionOption requirement)
        {
            Requirement = requirement;
        }

        /// <summary>
        /// 初始化 <see cref="TransactionAttribute"/> 类的新实例。
        /// </summary>
        /// <param name="alias">标记此特性的方法所要求的事务行为的别名。</param>
        public TransactionAttribute(TransactionOptionAlias alias)
        {
            Requirement = (TransactionOption)(int)alias;
        }

        /// <summary>
        /// 获取标记此特性的方法所要求的事务行为。
        /// </summary>
        public TransactionOption Requirement { get; }

        /// <summary>
        /// 获取或设置指示事务是否仅允许读取操作的值。
        /// </summary>
        [Obsolete("Use [ReadOnly] attribute instead.")]
        public bool ReadOnly { get; set; }
    }

    /// <summary>
    /// UseExclusiveLock 特性用于标记即使在读取操作中也应获取
    /// 排他锁的事务方法。这可避免高争用情况下频繁发生的锁升级冲突。
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
    /// 指定方法调用或委托如何与环境事务交互。
    /// </summary>
    public enum TransactionOption
    {
        /// <summary>
        /// 执行 Grain 调用时不传播环境事务；执行委托时，若存在环境事务则拒绝执行。
        /// </summary>
        Suppress,

        /// <summary>
        /// 如果存在环境事务，则加入该事务；否则创建事务。
        /// </summary>
        CreateOrJoin,

        /// <summary>
        /// 独立于任何环境事务创建新事务。
        /// </summary>
        Create,

        /// <summary>
        /// 加入环境事务，并要求环境事务必须存在。
        /// </summary>
        Join,

        /// <summary>
        /// 如果存在环境事务，则传播该事务；否则在无事务的情况下执行。
        /// </summary>
        Supported,

        /// <summary>
        /// 在无事务的情况下执行，并拒绝在环境事务中发起的调用。
        /// </summary>
        NotAllowed
    }

    /// <summary>
    /// 提供 <see cref="TransactionOption"/> 值的兼容性别名。
    /// </summary>
    public enum TransactionOptionAlias
    {
        /// <summary>
        /// 映射到 <see cref="TransactionOption.Supported"/>。
        /// </summary>
        Suppress = TransactionOption.Supported,

        /// <summary>
        /// 映射到 <see cref="TransactionOption.CreateOrJoin"/>。
        /// </summary>
        Required = TransactionOption.CreateOrJoin,

        /// <summary>
        /// 映射到 <see cref="TransactionOption.Create"/>。
        /// </summary>
        RequiresNew = TransactionOption.Create,

        /// <summary>
        /// 映射到 <see cref="TransactionOption.Join"/>。
        /// </summary>
        Mandatory = TransactionOption.Join,

        /// <summary>
        /// 映射到 <see cref="TransactionOption.NotAllowed"/>。
        /// </summary>
        Never = TransactionOption.NotAllowed,
    }

    /// <summary>
    /// 为生成的 Grain 请求调用器提供事务传播和决议功能。
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
        /// 获取或设置请求的事务行为。
        /// </summary>
        [Id(0)]
        public TransactionOption TransactionOption { get; set; }

        /// <summary>
        /// 获取或设置随请求传播的事务信息。
        /// </summary>
        [Id(1)]
        public TransactionInfo? TransactionInfo { get; set; }

        /// <summary>
        /// 获取或设置指示事务状态读取操作是否获取排他锁的值。
        /// </summary>
        [Id(2)]
        public bool UseExclusiveLock { get; set; }

        /// <summary>
        /// 初始化 <see cref="TransactionRequestBase"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">事务中止异常的序列化器。</param>
        /// <param name="serviceProvider">用于解析事务服务的服务提供程序。</param>
        [GeneratedActivatorConstructor]
        protected TransactionRequestBase(Serializer<OrleansTransactionAbortedException> exceptionSerializer, IServiceProvider serviceProvider)
        {
            _serializer = exceptionSerializer;

            // 可能为 null，例如在外部客户端上。如果调用时仍为 null，则抛出异常。
            _transactionAgent = serviceProvider.GetService<ITransactionAgent>();
        }

        /// <summary>
        /// 获取指示请求是否在环境事务之外执行的值。
        /// </summary>
        public bool IsAmbientTransactionSuppressed => TransactionOption switch
        {
            TransactionOption.Create => true,
            TransactionOption.Suppress => true,
            _ => false
        };

        /// <summary>
        /// 获取指示请求是否需要事务上下文的值。
        /// </summary>
        public bool IsTransactionRequired => TransactionOption switch
        {
            TransactionOption.Create => true,
            TransactionOption.CreateOrJoin => true,
            TransactionOption.Join => true,
            _ => false
        };

        /// <summary>
        /// 使用兼容性别名设置事务行为。
        /// </summary>
        /// <param name="txOption">事务选项别名。</param>
        protected void SetTransactionOptions(TransactionOptionAlias txOption) => SetTransactionOptions((TransactionOption)txOption);

        /// <summary>
        /// 设置请求的事务行为。
        /// </summary>
        /// <param name="txOption">事务选项。</param>
        protected void SetTransactionOptions(TransactionOption txOption)
        {
            this.TransactionOption = txOption;
        }

        /// <summary>
        /// 设置事务状态读取操作是否获取排他锁。
        /// </summary>
        /// <param name="value">
        /// <see langword="true"/> 表示读取时获取排他锁；否则为 <see langword="false"/>。
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
            // 如果事务操作要求创建新事务，则清除事务信息。
            var transactionInfo = TransactionContext.GetTransactionInfo();

            // 强制执行调用加入事务的要求。
            if (TransactionOption == TransactionOption.Join && transactionInfo == null)
            {
                throw new NotSupportedException("Call cannot be made outside of a transaction.");
            }

            // 强制执行不允许事务的调用要求。
            if (TransactionOption == TransactionOption.NotAllowed && transactionInfo != null)
            {
                throw new NotSupportedException("Call cannot be made within a transaction.");
            }

            // 如果要创建事务或抑制事务，则清除事务上下文。
            if (TransactionOption is TransactionOption.Create or TransactionOption.Suppress)
            {
                transactionInfo = null;
            }

            if (transactionInfo == null)
            {
                // 如果即将离开事务上下文，则确保已从请求上下文清除该事务上下文。
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
                    // TODO：这里应该使用可配置参数。
                    var transactionTimeout = Debugger.IsAttached ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(10);

                    // 启动新事务。
                    var isReadOnly = this.Options.HasFlag(InvokeMethodOptions.ReadOnly);
                    transactionInfo = await TransactionAgent.StartTransaction(isReadOnly, transactionTimeout);
                    startedNewTransaction = true;
                }

                // 如果特性要求，则设置此标志。
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
                    // 如果尚未设置中止原因，则记录中止原因。
                    transactionInfo.RecordException(invokeException, _serializer);
                }

                OrleansTransactionException? transactionException = transactionInfo.MustAbort(_serializer);

                // 此请求启动了事务，因此在返回前尝试提交；
                // 如果必须中止，则通知参与者事务已中止。
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
                response = null; // 返回的包装对象现在拥有调用响应的所有权。
                return result;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// 调用生成的请求实现。
        /// </summary>
        /// <returns>表示调用结果的响应。</returns>
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
    /// 将响应与调用产生的事务信息包装在一起。
    /// </summary>
    [GenerateSerializer]
    public sealed class TransactionResponse : Response
    {
        [Id(0)]
        private Response _response = null!;

        /// <summary>
        /// 获取或设置调用返回的事务信息。
        /// </summary>
        [Id(1)]
        public TransactionInfo? TransactionInfo { get; set; }

        /// <summary>
        /// 创建包装调用响应的事务响应。
        /// </summary>
        /// <param name="response">调用响应，其所有权转移给返回的包装对象。</param>
        /// <param name="transactionInfo">要随响应一起返回的事务信息。</param>
        /// <returns>事务响应；调用方通过释放它来释放被包装的响应。</returns>
        public static TransactionResponse Create(Response response, TransactionInfo transactionInfo)
        {
            return new TransactionResponse
            {
                _response = response,
                TransactionInfo = transactionInfo
            };
        }

        /// <summary>
        /// 获取被包装的调用响应。
        /// </summary>
        public Response InnerResponse => _response;

        /// <summary>
        /// 获取或设置被包装响应的结果。
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
        /// 获取 <see langword="null"/>，使包装对象能够作为成功响应投递；或者设置
        /// 被包装响应中的异常。
        /// </summary>
        public override Exception? Exception
        {
            get
            {
                // 在此处抑制任何异常，使 ResponseCompletionSource 能够以 Response 而不是异常完成。
                // 这样 TransactionRequestBase 就有机会先检查此实例并获取 TransactionInfo 属性。
                // 然后，它将使用 GetException 获取并抛出异常。
                return null;
            }

            set => _response.Exception = value;
        }

        /// <summary>
        /// 获取被包装响应中的异常。
        /// </summary>
        /// <returns>被包装的异常；如果调用成功完成，则为 <see langword="null"/>。</returns>
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
    /// 生成的事务请求调用器的基类型，这些调用器返回 <see cref="ValueTask"/>。
    /// </summary>
    [SerializerTransparent]
    public abstract class TransactionRequest : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionRequest"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">事务中止异常的序列化器。</param>
        /// <param name="serviceProvider">用于解析事务服务的服务提供程序。</param>
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
#pragma warning disable CA1849 // IsCompleted 证明此处能以非阻塞方式获取 ValueTask 的结果，包括失败或取消的情况。
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

        // 生成的代码
        /// <summary>
        /// 调用生成的方法实现。
        /// </summary>
        /// <returns>表示调用操作的任务。</returns>
        protected abstract ValueTask InvokeInner();
    }

    /// <summary>
    /// 生成的事务请求调用器的基类型，这些调用器返回 <see cref="ValueTask{TResult}"/>。
    /// </summary>
    /// <typeparam name="TResult">调用结果类型。</typeparam>
    [SerializerTransparent]
    public abstract class TransactionRequest<TResult> : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionRequest{TResult}"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">事务中止异常的序列化器。</param>
        /// <param name="serviceProvider">用于解析事务服务的服务提供程序。</param>
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

        // 生成的代码
        /// <summary>
        /// 调用生成的方法实现。
        /// </summary>
        /// <returns>包含调用结果的任务。</returns>
        protected abstract ValueTask<TResult> InvokeInner();
    }

    /// <summary>
    /// 生成的事务请求调用器的基类型，这些调用器返回 <see cref="Task{TResult}"/>。
    /// </summary>
    /// <typeparam name="TResult">调用结果类型。</typeparam>
    [SerializerTransparent]
    public abstract class TransactionTaskRequest<TResult> : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionTaskRequest{TResult}"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">事务中止异常的序列化器。</param>
        /// <param name="serviceProvider">用于解析事务服务的服务提供程序。</param>
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

        // 生成的代码
        /// <summary>
        /// 调用生成的方法实现。
        /// </summary>
        /// <returns>包含调用结果的任务。</returns>
        protected abstract Task<TResult> InvokeInner();
    }

    /// <summary>
    /// 生成的事务请求调用器的基类型，这些调用器返回 <see cref="Task"/>。
    /// </summary>
    [SerializerTransparent]
    public abstract class TransactionTaskRequest : TransactionRequestBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionTaskRequest"/> 类的新实例。
        /// </summary>
        /// <param name="exceptionSerializer">事务中止异常的序列化器。</param>
        /// <param name="serviceProvider">用于解析事务服务的服务提供程序。</param>
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
#pragma warning disable CA1849 // IsCompleted 证明此处能以非阻塞方式观察 Task 的结果，包括失败或取消的情况。
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

        // 生成的代码
        /// <summary>
        /// 调用生成的方法实现。
        /// </summary>
        /// <returns>表示调用操作的任务。</returns>
        protected abstract Task InvokeInner();
    }
}

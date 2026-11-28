using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 可完成的承诺对象（Promise）。
    /// </summary>
    public sealed class ResponseCompletionSource : IResponseCompletionSource, IValueTaskSource<Response>, IValueTaskSource
    {
        // 此完成源采用池化机制，GetResult 会将它归还到池中。延续操作不得在 SetResult/SetException 中内联执行，
        // 否则可能在完成操作退出调用栈之前重置或复用该实例。
        private ManualResetValueTaskSourceCore<Response> _core = new() { RunContinuationsAsynchronously = true };

        /// <summary>
        /// 以 <see cref="ValueTask{Response}"/> 形式返回此实例。
        /// </summary>
        /// <returns>以 <see cref="ValueTask{Response}"/> 形式返回的此实例。</returns>
        public ValueTask<Response> AsValueTask() => new(this, _core.Version);

        /// <summary>
        /// 以 <see cref="ValueTask"/> 形式返回此实例。
        /// </summary>
        /// <returns>以 <see cref="ValueTask"/> 形式返回的此实例。</returns>
        public ValueTask AsVoidValueTask() => new(this, _core.Version);

        /// <inheritdoc/>
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        /// <inheritdoc/>
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        /// <summary>
        /// 重置此实例。
        /// </summary>
        public void Reset()
        {
            _core.Reset();
            ResponseCompletionSourcePool.Return(this);
        }

        /// <summary>
        /// 使用异常完成此实例。
        /// </summary>
        /// <param name="exception">异常。</param>
        public void SetException(Exception exception) => _core.SetException(exception);

        /// <summary>
        /// 使用结果完成此实例。
        /// </summary>
        /// <param name="result">结果。</param>
        public void SetResult(Response result)
        {
            if (result.Exception is not { } exception)
            {
                _core.SetResult(result);
            }
            else
            {
                _core.SetException(exception);
            }
        }

        /// <summary>
        /// 使用结果完成此实例。
        /// </summary>
        /// <param name="value">结果值。</param>
        public void Complete(Response value) => SetResult(value);

        /// <summary>
        /// 使用默认结果完成此实例。
        /// </summary>
        public void Complete() => SetResult(Response.Completed);

        /// <inheritdoc />
        public Response GetResult(short token)
        {
            bool isValid = token == _core.Version;
            try
            {
                return _core.GetResult(token);
            }
            finally
            {
                if (isValid)
                {
                    Reset();
                }
            }
        }

        /// <inheritdoc />
        void IValueTaskSource.GetResult(short token)
        {
            bool isValid = token == _core.Version;
            try
            {
                _ = _core.GetResult(token);
            }
            finally
            {
                if (isValid)
                {
                    Reset();
                }
            }
        }
    }

    /// <summary>
    /// 可完成的承诺对象（Promise）。
    /// </summary>
    /// <typeparam name="TResult">底层结果类型。</typeparam>
    public sealed class ResponseCompletionSource<TResult> : IResponseCompletionSource, IValueTaskSource<TResult?>, IValueTaskSource
    {
        // 此完成源采用池化机制，GetResult 会将它归还到池中。延续操作不得在 SetResult/SetException 中内联执行，
        // 否则可能在完成操作退出调用栈之前重置或复用该实例。
        private ManualResetValueTaskSourceCore<TResult?> _core = new() { RunContinuationsAsynchronously = true };

        /// <summary>
        /// 以 <see cref="ValueTask{Response}"/> 形式返回此实例。
        /// </summary>
        /// <returns>以 <see cref="ValueTask{Response}"/> 形式返回的此实例。</returns>
        public ValueTask<TResult?> AsValueTask() => new(this, _core.Version);

        /// <summary>
        /// 以 <see cref="ValueTask"/> 形式返回此实例。
        /// </summary>
        /// <returns>以 <see cref="ValueTask"/> 形式返回的此实例。</returns>
        public ValueTask AsVoidValueTask() => new(this, _core.Version);

        /// <inheritdoc/>
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        /// <inheritdoc/>
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        /// <summary>
        /// 重置此实例。
        /// </summary>
        public void Reset()
        {
            _core.Reset();
            ResponseCompletionSourcePool.Return(this);
        }

        /// <summary>
        /// 使用异常完成此实例。
        /// </summary>
        /// <param name="exception">异常。</param>
        public void SetException(Exception exception) => _core.SetException(exception);

        /// <summary>
        /// 使用结果完成此实例。
        /// </summary>
        /// <param name="result">结果。</param>
        public void SetResult(TResult? result) => _core.SetResult(result);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete(Response value)
        {
            // 先检查异常，因为这里只需进行简单的 null 检查。
            if (value.Exception is { } exception)
            {
                SetException(exception);
                return;
            }

            // 检查强类型响应（对于 void 返回值很常见）。
            if (value is Response<TResult> typed)
            {
                SetResult(typed.TypedResult);
                return;
            }

            // 处理无类型的成功响应。
            var result = value.Result;
            if (result is null)
            {
                SetResult(default);
            }
            else if (result is TResult typedResult)
            {
                SetResult(typedResult);
            }
            else
            {
                SetInvalidCastException(result);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetInvalidCastException(object result)
        {
            var exception = new InvalidCastException($"Cannot cast object of type {result.GetType()} to {typeof(TResult)}");
#if NET5_0_OR_GREATER
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetCurrentStackTrace(exception);
            SetException(exception);
#else
            try
            {
                throw exception;
            }
            catch (Exception ex)
            {
                SetException(ex);
            }
#endif
        }

        /// <summary>
        /// 使用结果完成此实例。
        /// </summary>
        /// <param name="value">结果值。</param>
        public void Complete(Response<TResult> value)
        {
            if (value.Exception is { } exception)
            {
                SetException(exception);
            }
            else
            {
                SetResult(value.TypedResult);
            }
        }

        /// <inheritdoc/>
        public void Complete() => SetResult(default);

        /// <inheritdoc/>
        public TResult? GetResult(short token)
        {
            bool isValid = token == _core.Version;
            try
            {
                return _core.GetResult(token);
            }
            finally
            {
                if (isValid)
                {
                    Reset();
                }
            }
        }

        /// <inheritdoc/>
        void IValueTaskSource.GetResult(short token)
        {
            bool isValid = token == _core.Version;
            try
            {
                _ = _core.GetResult(token);
            }
            finally
            {
                if (isValid)
                {
                    Reset();
                }
            }
        }
    }
}

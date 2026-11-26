using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Orleans.Serialization.Invocation
{
    /// <summary>
    /// 能够兑现的承诺
    /// </summary>
    public sealed class ResponseCompletionSource : IResponseCompletionSource, IValueTaskSource<Response>, IValueTaskSource
    {
        // 此源是集合的, GetResult 返回到集合。 续发不能从 SetResult/SetException内线运行 ,
        // 或者,它们可以在完成后卸下时段前重置/重复此实例 。
        private ManualResetValueTaskSourceCore<Response> _core = new() { RunContinuationsAsynchronously = true };

        /// <summary>
        /// 此实例以 <see cref="ValueTask{Response}"/>返回。
        /// </summary>
        /// <returns>此例, 作为 <see cref="ValueTask{Response}"/>。</returns>
        public ValueTask<Response> AsValueTask() => new(this, _core.Version);

        /// <summary>
        /// 此实例以 <see cref="ValueTask"/>返回。
        /// </summary>
        /// <returns>此例, 作为 <see cref="ValueTask"/>。</returns>
        public ValueTask AsVoidValueTask() => new(this, _core.Version);

        /// <inheritdoc/>
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        /// <inheritdoc/>
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        /// <summary>
        /// 重设此实例 。
        /// </summary>
        public void Reset()
        {
            _core.Reset();
            ResponseCompletionSourcePool.Return(this);
        }

        /// <summary>
        /// 完成此例, 但有例外 。
        /// </summary>
        /// <param name="exception">例外。</param>
        public void SetException(Exception exception) => _core.SetException(exception);

        /// <summary>
        /// 完成此实例并产生结果。
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
        /// 完成此实例并产生结果。
        /// </summary>
        /// <param name="value">结果值。</param>
        public void Complete(Response value) => SetResult(value);

        /// <summary>
        /// 以默认结果完成此实例。
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
    /// 能够兑现的承诺
    /// </summary>
    /// <typeparam name="TResult">基本结果类型。</typeparam>
    public sealed class ResponseCompletionSource<TResult> : IResponseCompletionSource, IValueTaskSource<TResult?>, IValueTaskSource
    {
        // 此源是集合的, GetResult 返回到集合。 续发不能从 SetResult/SetException内线运行 ,
        // 或者,它们可以在完成后卸下时段前重置/重复此实例 。
        private ManualResetValueTaskSourceCore<TResult?> _core = new() { RunContinuationsAsynchronously = true };

        /// <summary>
        /// 此实例以 <see cref="ValueTask{Response}"/>返回。
        /// </summary>
        /// <returns>此例, 作为 <see cref="ValueTask{Response}"/>。</returns>
        public ValueTask<TResult?> AsValueTask() => new(this, _core.Version);

        /// <summary>
        /// 此实例以 <see cref="ValueTask"/>返回。
        /// </summary>
        /// <returns>此例, 作为 <see cref="ValueTask"/>。</returns>
        public ValueTask AsVoidValueTask() => new(this, _core.Version);

        /// <inheritdoc/>
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        /// <inheritdoc/>
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);

        /// <summary>
        /// 重设此实例 。
        /// </summary>
        public void Reset()
        {
            _core.Reset();
            ResponseCompletionSourcePool.Return(this);
        }

        /// <summary>
        /// 完成此例, 但有例外 。
        /// </summary>
        /// <param name="exception">例外。</param>
        public void SetException(Exception exception) => _core.SetException(exception);

        /// <summary>
        /// 完成此实例并产生结果。
        /// </summary>
        /// <param name="result">结果。</param>
        public void SetResult(TResult? result) => _core.SetResult(result);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete(Response value)
        {
            // 首选例外, 因为它是一个简单的空检查
            if (value.Exception is { } exception)
            {
                SetException(exception);
                return;
            }

            // 检查打字响应( 无效返回通常)
            if (value is Response<TResult> typed)
            {
                SetResult(typed.TypedResult);
                return;
            }

            // 处理未类型化的成功响应
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
        /// 完成此实例并产生结果。
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

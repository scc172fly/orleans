using System;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime
{
    /// <summary>
    /// 管理当前 grain 背景的职能。
    /// </summary>
    internal static class RuntimeContext
    {
        /// <summary>
        /// 线形本地背景。
        /// </summary>
        [ThreadStatic]
        private static IGrainContext? _threadLocalContext;

        /// <summary>
        /// 获取当前 grain 上下文。
        /// </summary>
        public static IGrainContext? Current => _threadLocalContext;

        /// <summary>
        /// 设置当前 grain 上下文。
        /// </summary>
        /// <param name="newContext">新上下文。</param>
        /// <param name="currentContext">呼叫时的当前上下文。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetExecutionContext(IGrainContext newContext, out IGrainContext? currentContext)
        {
            currentContext = _threadLocalContext;
            _threadLocalContext = newContext;
        }

        /// <summary>
        /// 将当前 grain 上下文重置到所提供的原始上下文。
        /// </summary>
        /// <param name="originalContext">原始背景。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ResetExecutionContext(IGrainContext? originalContext)
        {
            _threadLocalContext = originalContext;
        }
    }
}

using System;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于管理当前 Grain 上下文的功能。
    /// </summary>
    internal static class RuntimeContext
    {
        /// <summary>
        /// 线程局部上下文。
        /// </summary>
        [ThreadStatic]
        private static IGrainContext? _threadLocalContext;

        /// <summary>
        /// 获取当前 Grain 上下文。
        /// </summary>
        public static IGrainContext? Current => _threadLocalContext;

        /// <summary>
        /// 设置当前 Grain 上下文。
        /// </summary>
        /// <param name="newContext">新的上下文。</param>
        /// <param name="currentContext">调用时的当前上下文。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetExecutionContext(IGrainContext newContext, out IGrainContext? currentContext)
        {
            currentContext = _threadLocalContext;
            _threadLocalContext = newContext;
        }

        /// <summary>
        /// 将当前 Grain 上下文重置为提供的原始上下文。
        /// </summary>
        /// <param name="originalContext">原始上下文。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ResetExecutionContext(IGrainContext? originalContext)
        {
            _threadLocalContext = originalContext;
        }
    }
}

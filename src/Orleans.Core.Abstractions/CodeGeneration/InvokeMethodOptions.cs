using System;

namespace Orleans.CodeGeneration
{
    /// <summary>
    /// <c>InvokeMethodRequest</c> 的调用选项。
    /// </summary>
    /// <remarks>
    /// Orleans 生成的调用器代码会使用这些标志值，因此不应更改它们。</remarks>
    [Flags]
    [GenerateSerializer]
    public enum InvokeMethodOptions
    {
        /// <summary>未指定选项。</summary>
        None = 0,

        /// <summary>单向调用，不反馈调用是否成功。</summary>
        OneWay = 1 << 0,

        /// <summary>只读调用，可与其他只读调用交错执行。</summary>
        ReadOnly = 1 << 1,

        /// <summary>调用可与任何其他类型的请求交错执行，包括写入请求。</summary>
        AlwaysInterleave = 1 << 2,

        /// <summary>调用不要求顺序，因此可以进行相应优化。</summary>
        Unordered = 1 << 3,
    }
}

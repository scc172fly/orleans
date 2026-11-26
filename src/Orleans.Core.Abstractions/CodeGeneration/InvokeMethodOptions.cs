using System;

namespace Orleans.CodeGeneration
{
    /// <summary>
    /// 启动 <c>InvokeMethodRequest</c>的调用选项
    /// </summary>
    /// <remarks>
    /// 这些标志值用于生成的 Orleans 引用代码,不应更改。 </remarks>
    [Flags]
    [GenerateSerializer]
    public enum InvokeMethodOptions
    {
        /// <summary>未定义选项。</summary>
        None = 0,

        /// <summary>援引是单向的,没有关于呼叫是否成功或失败的反馈。</summary>
        OneWay = 1 << 0,

        /// <summary>援引是只读,可与其他只读的援引发生间断。</summary>
        ReadOnly = 1 << 1,

        /// <summary>援引可与任何其他类型的请求,包括书面请求发生间断。</summary>
        AlwaysInterleave = 1 << 2,

        /// <summary>援引并不关心订购,因此可以优化。</summary>
        Unordered = 1 << 3,
    }
}

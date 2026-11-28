using System;

namespace Orleans
{
    /// <summary>
    /// 定义 Grain 的状态。
    /// </summary>
    /// <typeparam name="T">
    /// 底层状态类型。
    /// </typeparam>
    public interface IGrainState<T>
    {
        /// <summary>
        /// 获取或设置状态。
        /// </summary>
        T? State { get; set; }

        /// <summary>获取或设置 ETag，用于在存储提供程序层执行乐观并发检查。</summary>
        string? ETag { get; set; }

        /// <summary>
        /// 获取或设置指示记录是否存在于存储中的值。
        /// </summary>
        bool RecordExists { get; set; }
    }

    /// <summary>
    /// <see cref="IGrainState{T}"/> 的默认实现。
    /// </summary>
    /// <typeparam name="T">应用层有效载荷的类型。</typeparam>
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainState<T> : IGrainState<T>
    {
        /// <inheritdoc />
        [Id(0)]
        public T? State { get; set; }

        /// <inheritdoc />
        [Id(1)]
        public string? ETag { get; set; }

        /// <inheritdoc />
        [Id(2)]
        public bool RecordExists { get; set; }

        /// <summary>
        /// 初始化 <see cref="GrainState{T}"/> 类的新实例。 
        /// </summary>
        public GrainState()
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainState{T}"/> 类的新实例。 
        /// </summary>
        /// <param name="state">
        /// 状态的初始值。
        /// </param>
        public GrainState(T? state) : this(state, null)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainState{T}"/> 类的新实例。
        /// </summary>
        /// <param name="state">
        /// 状态的初始值。
        /// </param>
        /// <param name="eTag">
        /// 用于在存储提供程序层执行乐观并发检查的初始 ETag 值。
        /// </param>
        public GrainState(T? state, string? eTag)
        {
            State = state;
            ETag = eTag;
        }
    }
}

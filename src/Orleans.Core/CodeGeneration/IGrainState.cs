using System;

namespace Orleans
{
    /// <summary>
    /// 定义 grain状态
    /// </summary>
    /// <typeparam name="T">
    /// 基本状态类型。
    /// </typeparam>
    public interface IGrainState<T>
    {
        /// <summary>
        /// 建立州立或控制
        /// </summary>
        T? State { get; set; }

        /// <summary>获取或设置允许在储存提供者一级进行乐观的货币汇率核对的ETag。</summary>
        string? ETag { get; set; }

        /// <summary>
        /// 获取或设定一个值,以显示记录是否存于存储中。
        /// </summary>
        bool RecordExists { get; set; }
    }

    /// <summary>
    /// <see cref="IGrainState{T}"/>的默认执行。
    /// </summary>
    /// <typeparam name="T">应用水平有效载荷的类型。</typeparam>
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
        /// 状态的初始值 。
        /// </param>
        public GrainState(T? state) : this(state, null)
        {
        }

        /// <summary>
        /// 初始化 <see cref="GrainState{T}"/> 类的新实例。
        /// </summary>
        /// <param name="state">
        /// 状态的初始值 。
        /// </param>
        /// <param name="eTag">
        /// 最初的电子牌值,可以在储存提供者一级进行乐观的货币汇率核对。
        /// </param>
        public GrainState(T? state, string? eTag)
        {
            State = state;
            ETag = eTag;
        }
    }
}

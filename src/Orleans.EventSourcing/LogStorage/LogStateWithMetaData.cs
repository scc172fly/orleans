using Orleans.EventSourcing.Common;
using System;
using System.Collections.Generic;

namespace Orleans.EventSourcing.LogStorage
{
    /// <summary>
    /// 扩展的类 grain 状态与元数据版本的元数据,因此 a grain 具有日志一致性
    /// 使用标准的储存提供者。
    /// </summary>
    /// <typeparam name="TEntry">日志条目使用的类型</typeparam>
    [Serializable]
    [GenerateSerializer]
    public sealed class LogStateWithMetaDataAndETag<TEntry> : IGrainState<LogStateWithMetaData<TEntry>> where TEntry : class
    {
        /// <summary>
        /// 获取和设置 StateAndMetaData
        /// </summary>
        [Id(0)]
        public LogStateWithMetaData<TEntry> StateAndMetaData
        {
            get => field ??= new LogStateWithMetaData<TEntry>();
            set => field = value ?? new LogStateWithMetaData<TEntry>();
        }

        /// <summary>
        /// 获取和设置标签
        /// </summary>
        [Id(1)]
        public string? ETag { get; set; }

        /// <inheritdoc />
        [Id(2)]
        public bool RecordExists { get; set; }

        /// <inheritdoc />
        public LogStateWithMetaData<TEntry> State { get => StateAndMetaData; set => StateAndMetaData = value; }

        LogStateWithMetaData<TEntry>? IGrainState<LogStateWithMetaData<TEntry>>.State
        {
            get => State;
            set => StateAndMetaData = value ?? new LogStateWithMetaData<TEntry>();
        }

        /// <summary>
        /// 初始化 GrainStateWithMetaDataAndETag 类的新实例
        /// </summary>
        public LogStateWithMetaDataAndETag()
        {
            StateAndMetaData = new LogStateWithMetaData<TEntry>();
        }

        /// <summary>
        /// 将当前 GrainStateWithMetaDataAndETag 对象信息转换为字符串
        /// </summary>
        public override string ToString()
        {
            return string.Format("v{0} Flags={1} ETag={2} Data={3}", StateAndMetaData.GlobalVersion, StateAndMetaData.WriteVector, ETag, StateAndMetaData.Log);
        }
    }

    /// <summary>
    /// 包含元元元版本的扩展 grain 状态的分类,以便符合日志的 grain
    /// 使用标准的储存提供者。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class LogStateWithMetaData<TEntry> where TEntry : class
    {
        /// <summary>
        /// 日志的存储视图
        /// </summary>
        [Id(0)]
        public List<TEntry> Log { get; set; }

        /// <summary>
        /// 日志长度
        /// </summary>
        public int GlobalVersion { get { return Log.Count; } }

        /// <summary>
        /// 用于避免重复附件的元数据。
        /// 逻辑上, 这是一张( 字符串- >bit) 映射图, 键是复制代码
        /// 但这张地图的缩写 简单代表一个字符串 以减少序列化/后级化间接费用
        /// 位数由 <see cref="GetBit"/> 读取,而由 <see cref="FlipBit"/>翻转。
        /// 写时会将位元混在一起, 这样重试逻辑可以避免两次附加条目
        /// 重试失败的附加件时。
        /// </summary>
        [Id(1)]
        public string WriteVector { get; set; }

        /// <summary>
        /// 初始化 <see cref="LogStateWithMetaData{TView}"/> 类的新实例。
        /// </summary>
        public LogStateWithMetaData()
        {
            Log = [];
            WriteVector = "";
        }

        /// <summary>
        /// 在 <see cref="WriteVector"/>中获取一个位数
        /// </summary>
        /// <param name="replica">我们希望查找位数的复制件</param>
        /// <returns></returns>
        public bool GetBit(string replica)
        {
            return StringEncodedWriteVector.GetBit(WriteVector, replica);
        }

        /// <summary>
        /// 切换 <see cref="WriteVector"/> 中的比特之一并返回新值。
        /// </summary>
        /// <param name="replica">复制我们想要翻转位</param>
        /// <returns>翻转时位数的状态</returns>
        public bool FlipBit(string replica)
        {
            var str = WriteVector;
            var result = StringEncodedWriteVector.FlipBit(ref str, replica);
            WriteVector = str;
            return result;
        }
    }
}

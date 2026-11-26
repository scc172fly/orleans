using Orleans.EventSourcing.Common;
using System;

namespace Orleans.EventSourcing.StateStorage
{
    /// <summary>
    /// 扩展的类 grain 状态与元数据版本的元数据,因此 a grain 具有日志一致性
    /// 使用标准的储存提供者。
    /// </summary>
    /// <typeparam name="TView">日志视图</typeparam>所用类型
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainStateWithMetaDataAndETag<TView> : IGrainState<GrainStateWithMetaData<TView>> where TView : class, new()
    {
        /// <summary>
        /// 获取和设置 StateAndMetaData
        /// </summary>
        [Id(0)]
        public GrainStateWithMetaData<TView> StateAndMetaData
        {
            get => field ??= new GrainStateWithMetaData<TView>();
            set => field = value ?? new GrainStateWithMetaData<TView>();
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
        public GrainStateWithMetaData<TView> State { get => StateAndMetaData; set => StateAndMetaData = value; }

        GrainStateWithMetaData<TView>? IGrainState<GrainStateWithMetaData<TView>>.State
        {
            get => State;
            set => StateAndMetaData = value ?? new GrainStateWithMetaData<TView>();
        }

        /// <summary>
        /// 初始化一个 GrainStateWithMetaDataAndETag 类新实例,其中带有 initialView
        /// </summary>
        public GrainStateWithMetaDataAndETag(TView initialview)
        {
            StateAndMetaData = new GrainStateWithMetaData<TView>(initialview);
        }

        /// <summary>
        /// 初始化 GrainStateWithMetaDataAndETag 类的新实例
        /// </summary>
        public GrainStateWithMetaDataAndETag()
        {
            StateAndMetaData = new GrainStateWithMetaData<TView>();
        }

        /// <summary>
        /// 将当前 GrainStateWithMetaDataAndETag 对象信息转换为字符串
        /// </summary>
        public override string ToString()
        {
            return string.Format("v{0} Flags={1} ETag={2} Data={3}", StateAndMetaData.GlobalVersion, StateAndMetaData.WriteVector, ETag, StateAndMetaData.State);
        }
    }


    /// <summary>
    /// 包含元元元版本的扩展 grain 状态的分类,以便符合日志的 grain
    /// 使用标准的储存提供者。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainStateWithMetaData<TView> where TView : class, new()
    {
        /// <summary>
        /// 日志的存储视图
        /// </summary>
        [Id(0)]
        public TView State { get; set; }

        /// <summary>
        /// 日志长度
        /// </summary>
        [Id(1)]
        public int GlobalVersion { get; set; }


        /// <summary>
        /// 用于避免重复附件的元数据。
        /// 逻辑上, 这是一张( 字符串- >bit) 映射图, 键是复制代码
        /// 但这张地图的缩写 简单代表一个字符串 以减少序列化/后级化间接费用
        /// 位数由 <see cref="GetBit"/> 读取,而由 <see cref="FlipBit"/>翻转。
        /// 写时会将位元混在一起, 这样重试逻辑可以避免两次附加条目
        /// 重试失败的附加件时。
        /// </summary>
        [Id(2)]
        public string WriteVector { get; set; }

        /// <summary>
        /// 初始化 <see cref="GrainStateWithMetaData{TView}"/> 类的新实例。
        /// </summary>
        public GrainStateWithMetaData()
        {
            State = new TView();
            GlobalVersion = 0;
            WriteVector = "";
        }

        /// <summary>
        /// 初始化 <see cref="GrainStateWithMetaData{TView}"/> 类的新实例。
        /// </summary>
        /// <param name="initialstate">视图的初始状态</param>
        public GrainStateWithMetaData(TView initialstate)
        {
            this.State = initialstate;
            GlobalVersion = 0;
            WriteVector = "";
        }


        /// <summary>
        /// 在 <see cref="WriteVector"/>中获取一个位数
        /// </summary>
        /// <param name="Replica">我们希望查找位数的复制件</param>
        /// <returns></returns>
        public bool GetBit(string Replica)
        {
            return StringEncodedWriteVector.GetBit(WriteVector, Replica);
        }

        /// <summary>
        /// 切换 <see cref="WriteVector"/> 中的比特之一并返回新值。
        /// </summary>
        /// <param name="Replica">复制我们想要翻转位</param>
        /// <returns>翻转时位数的状态</returns>
        public bool FlipBit(string Replica)
        {
            var str = WriteVector;
            var rval = StringEncodedWriteVector.FlipBit(ref str, Replica);
            WriteVector = str;
            return rval;
        }
    }
}

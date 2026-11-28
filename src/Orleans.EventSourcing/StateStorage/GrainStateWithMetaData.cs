using Orleans.EventSourcing.Common;
using System;

namespace Orleans.EventSourcing.StateStorage
{
    /// <summary>
    /// 使用版本元数据扩展 Grain 状态的类，使具有日志视图一致性的 Grain
    /// 可以使用标准存储提供程序。
    /// </summary>
    /// <typeparam name="TView">日志视图使用的类型。</typeparam>
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainStateWithMetaDataAndETag<TView> : IGrainState<GrainStateWithMetaData<TView>> where TView : class, new()
    {
        /// <summary>
        /// 获取或设置 StateAndMetaData。
        /// </summary>
        [Id(0)]
        public GrainStateWithMetaData<TView> StateAndMetaData
        {
            get => field ??= new GrainStateWithMetaData<TView>();
            set => field = value ?? new GrainStateWithMetaData<TView>();
        }

        /// <summary>
        /// 获取或设置 Etag。
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
        /// 使用 initialView 初始化 GrainStateWithMetaDataAndETag 类的新实例。
        /// </summary>
        public GrainStateWithMetaDataAndETag(TView initialview)
        {
            StateAndMetaData = new GrainStateWithMetaData<TView>(initialview);
        }

        /// <summary>
        /// 初始化 GrainStateWithMetaDataAndETag 类的新实例。
        /// </summary>
        public GrainStateWithMetaDataAndETag()
        {
            StateAndMetaData = new GrainStateWithMetaData<TView>();
        }

        /// <summary>
        /// 将当前 GrainStateWithMetaDataAndETag 对象的信息转换为字符串。
        /// </summary>
        public override string ToString()
        {
            return string.Format("v{0} Flags={1} ETag={2} Data={3}", StateAndMetaData.GlobalVersion, StateAndMetaData.WriteVector, ETag, StateAndMetaData.State);
        }
    }


    /// <summary>
    /// 使用版本元数据扩展 Grain 状态的类，使具有日志一致性的 Grain
    /// 可以使用标准存储提供程序。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class GrainStateWithMetaData<TView> where TView : class, new()
    {
        /// <summary>
        /// 已存储的日志视图。
        /// </summary>
        [Id(0)]
        public TView State { get; set; }

        /// <summary>
        /// 日志的长度。
        /// </summary>
        [Id(1)]
        public int GlobalVersion { get; set; }


        /// <summary>
        /// 用于避免重复追加的元数据。
        /// 从逻辑上看，这是一个从字符串到位的映射，键为副本 ID。
        /// 但为减少序列化及反序列化开销，此映射以一个简单字符串紧凑表示。
        /// 通过 <see cref="GetBit"/> 读取位，通过 <see cref="FlipBit"/> 翻转位。
        /// 写入时翻转位，以使重试逻辑在重试失败的追加操作时
        /// 避免将同一条目追加两次。
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
        /// <param name="initialstate">视图的初始状态。</param>
        public GrainStateWithMetaData(TView initialstate)
        {
            this.State = initialstate;
            GlobalVersion = 0;
            WriteVector = "";
        }


        /// <summary>
        /// 获取 <see cref="WriteVector"/> 中的某一位。
        /// </summary>
        /// <param name="Replica">要查询其对应位的副本。</param>
        /// <returns></returns>
        public bool GetBit(string Replica)
        {
            return StringEncodedWriteVector.GetBit(WriteVector, Replica);
        }

        /// <summary>
        /// 翻转 <see cref="WriteVector"/> 中的某一位，并返回新值。
        /// </summary>
        /// <param name="Replica">要翻转其对应位的副本。</param>
        /// <returns>翻转后该位的状态。</returns>
        public bool FlipBit(string Replica)
        {
            var str = WriteVector;
            var rval = StringEncodedWriteVector.FlipBit(ref str, Replica);
            WriteVector = str;
            return rval;
        }
    }
}

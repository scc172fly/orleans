using System;
using System.Runtime.Serialization;

namespace Orleans.Storage
{
    /// <summary>
    /// 当储存提供者在试图进行 WriteStateAsync 操作时发现Etag不一致时,就提出例外。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class TableStorageUpdateConditionNotSatisfiedException : InconsistentStateException
    {
        private const string DefaultMessageFormat = "Table storage condition not Satisfied.  GrainType: {0}, GrainId: {1}, TableName: {2}, StoredETag: {3}, CurrentETag: {4}";

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        public TableStorageUpdateConditionNotSatisfiedException(
            string errorMsg,
            string grainType,
            string grainId,
            string tableName,
            string storedEtag,
            string? currentEtag,
            Exception storageException)
            : base(errorMsg, storedEtag, currentEtag, storageException)
        {
            this.GrainType = grainType;
            this.GrainId = grainId;
            this.TableName = tableName;
        }

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        public TableStorageUpdateConditionNotSatisfiedException(
            string grainType,
            string grainId,
            string tableName,
            string storedEtag,
            string? currentEtag,
            Exception storageException)
            : this(CreateDefaultMessage(grainType, grainId, tableName, storedEtag, currentEtag), grainType, grainId, tableName, storedEtag, currentEtag, storageException)
        {
        }

        /// <summary>
        /// grain日编号
        /// </summary>
        [Id(0)]
        public string? GrainId { get; }

        /// <summary>
        /// 丢弃此例外的 grain 类型
        /// </summary>
        [Id(1)]
        public string? GrainType { get; }

        /// <summary>
        /// Azure 表格名称
        /// </summary>
        [Id(2)]
        public string? TableName { get; }

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        public TableStorageUpdateConditionNotSatisfiedException()
        {
        }

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        public TableStorageUpdateConditionNotSatisfiedException(string msg)
            : base(msg)
        {
        }

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        public TableStorageUpdateConditionNotSatisfiedException(string msg, Exception exc)
            : base(msg, exc)
        {
        }

        private static string CreateDefaultMessage(
            string grainType,
            string grainId,
            string tableName,
            string storedEtag,
            string? currentEtag)
        {
            return string.Format(DefaultMessageFormat, grainType, grainId, tableName, storedEtag, currentEtag);
        }

        /// <summary>
        /// 冷冻桌储存例外因更新条件不能满足而被扔出例外。
        /// </summary>
        [Obsolete]
        protected TableStorageUpdateConditionNotSatisfiedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.GrainType = info.GetString("GrainType");
            this.GrainId = info.GetString("GrainId");
            this.TableName = info.GetString("TableName");
        }

        /// <inheritdoc />   
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue("GrainType", this.GrainType);
            info.AddValue("GrainId", this.GrainId);
            info.AddValue("TableName", this.TableName);
            base.GetObjectData(info, context);
        }
    }
}

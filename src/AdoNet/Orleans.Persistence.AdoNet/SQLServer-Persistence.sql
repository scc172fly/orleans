-- 共享的 Main 脚本创建 OrleansQuery 后，应用此脚本以执行初始化及可重复升级。
-- 应用脚本前，停止所有使用此数据库的 Silo，之后重新启动以重新加载查询。
--
-- 此表的设计准则如下：
--
-- 1. 能够存储序列化为二进制、XML 或 JSON 的任意内容。支持这些格式，
-- 是为了在需要时利用存储系统对相应类型的处理能力。
-- 这不应增加存储开销。
--
-- 2. 表的设计应能扩展至数十、数百（甚至更多）种
-- Grain 类型，每种类型可能对应数十万个 Grain ID，
-- 并支持这些 Grain 的运行。
--
-- 3. 表及其相关操作应保持稳定，不应因
-- 结构设计导致操作发生意外延迟。同时还应能
-- 较快地插入数据，而不发生资源争用。
--
-- 4. 基于第 2、3 点，索引应尽可能窄，以便充分驻留
-- 内存，并避免索引维护消耗过多资源。因此，
-- 索引特意设计得较窄（理想情况下为非聚集索引）。目前存储系统
-- 通过 Grain 类型及其 ID 识别实体，这两者的组合在 Orleans Silo 中唯一。
-- ID 由 Grain ID 的字节（若为字符串类型则为 UTF-8 字节）与可能存在的扩展键的 UTF-8
-- 字节拼接后计算哈希得到。
--
-- 使用哈希的原因：数据库引擎通常限制列的长度，
-- 这会人为限制 ID 或类型的长度。即使未超限，
-- 索引也会变得较宽并消耗更多内存。
--
-- 在当前设计中，ID 和类型各自哈希为一个 INT 类型的值，
-- 并构成复合索引。如果没有哈希冲突，索引可以快速定位
-- 唯一的行。除了哈希索引值，还存储 NVARCHAR(nnn) 值，
-- 以在发生哈希冲突时筛选出唯一的结果行。
--
-- 5. 此设计会导致存储中的数据重复。可以合理假设，
-- 任意时刻具有特定服务 ID 的运行中服务数量较少，或者
-- 相较于 Grain ID 的数量，
-- 不同的 Grain 类型数量相当少。如果将这些数据拆分到另一张表，则 INSERT
-- 和 UPDATE 操作会变得复杂，需要连接查询、临时变量、额外的
-- 索引或这些手段的组合才能实现。比较合适的策略
-- 可能是使用表压缩。
--
-- 6. 基于上述原因，Grain 状态的 DELETE 操作将数据字段设为 NULL，
-- 并正常更新 Version 版本号。这会牺牲少量存储空间，但应能减轻索引或
-- 统计信息的维护需求。可以在独立的维护操作中
-- 清理该表。
--
-- 7. 存储操作的查询结果中，各列的顺序必须完全一致，
-- 因为存储表操作可选择使用流式处理。
--
-- 8. Version 达到 2147483647 后，下一个值回绕至 1，因此频繁写入的 Grain 不会使
-- 32 位 Version 溢出。Orleans 仅比较版本是否相等，因此每次写入时版本号必须变化，
-- 但不需要始终递增。只有在对同一 Grain 又执行
-- 2147483647 次写入后，旧版本号才可能再次匹配。
IF OBJECT_ID(N'[OrleansStorage]', 'U') IS NULL
BEGIN
CREATE TABLE OrleansStorage
(
    -- 这些字段用于记录管理。Orleans 计算
    -- 这些哈希值（参见 RelationalStorageProvide 实现），
    -- 它们是映射到 *Hash 字段的有符号 32 位整数。
    -- 映射在代码中完成。
    -- *String 列包含相应的原始名称。
    --
    -- 若发生哈希冲突，则使用 GrainIdN0、
    -- GrainIdN1、GrainIdExtensionString 和 GrainTypeString 字段区分发生哈希冲突的记录。
    -- 预计很少需要这样做。
    GrainIdHash                INT NOT NULL,
    GrainIdN0                BIGINT NOT NULL,
    GrainIdN1                BIGINT NOT NULL,
    GrainTypeHash            INT NOT NULL,
    GrainTypeString            NVARCHAR(512) NOT NULL,
    GrainIdExtensionString    NVARCHAR(512) NULL,
    ServiceId                NVARCHAR(150) NOT NULL,

    -- 有效载荷。
    PayloadBinary    VARBINARY(MAX) NULL,

    -- 仅供参考的字段，无其他用途。
    ModifiedOn DATETIME2(3) NOT NULL,

    -- 已存储有效载荷的版本。
    Version INT NULL

    -- 以下字段原则上应构成主键，但作为索引会过宽，
    -- 因此对其值进行哈希，仅在发生哈希冲突时才
    -- 使用这些字段区分发生哈希冲突的记录。也就是说，在索引查询将匹配的行
    -- 缩小到 [0, n] 后（n 为发生冲突的值对数量），再使用这些字段。
);

IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_OrleansStorage' AND object_id = OBJECT_ID('OrleansStorage'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_OrleansStorage ON OrleansStorage(GrainIdHash, GrainTypeHash);
END

-- 这可确保锁升级不会锁定整个表，因为表可能非常大。
-- 更多信息见 https://www.littlekendra.com/2016/02/04/why-rowlock-hints-can-make-queries-slower-and-blocking-worse-in-sql-server/。
ALTER TABLE OrleansStorage SET(LOCK_ESCALATION = DISABLE);

-- 一种功能是压缩。如果受支持，则对 OrleansStorage 表启用压缩。这是企业版功能。
-- 这会消耗更多 CPU 周期，但应能减少 GrainIdString、GrainTypeString 和 ServiceId 的存储空间，
-- 因为这些字段主要包含重复值。有效载荷也会被压缩。
IF EXISTS (SELECT 1 FROM sys.dm_db_persisted_sku_features WHERE feature_id = 100)
BEGIN
    ALTER TABLE OrleansStorage REBUILD PARTITION = ALL WITH(DATA_COMPRESSION = PAGE);
END
END;

MERGE INTO OrleansQuery AS target
USING
(
SELECT
    'WriteToStorageKey' AS QueryKey,
    '-- When Orleans is running in normal, non-split state, there will
    -- be only one grain with the given ID and type combination only. This
    -- grain saves states mostly serially if Orleans guarantees are upheld. Even
    -- if not, the updates should work correctly due to version number.
    --
    -- In split brain situations there can be a situation where there are two or more
    -- grains with the given ID and type combination. When they try to INSERT
    -- concurrently, the table needs to be locked pessimistically before one of
    -- the grains gets @GrainStateVersion = 1 in return and the other grains will fail
    -- to update storage. The following arrangement is made to reduce locking in normal operation.
    --
    -- If the version number explicitly returned is still the same, Orleans interprets it so the update did not succeed
    -- and throws an InconsistentStateException.
    --
    -- See further information at https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence.
    BEGIN TRANSACTION;
    SET XACT_ABORT, NOCOUNT ON;

    DECLARE @NewGrainStateVersion AS INT = @GrainStateVersion;


    -- If the @GrainStateVersion is not zero, this branch assumes it exists in this database.
    -- The NULL value is supplied by Orleans when the state is new.
    IF @GrainStateVersion IS NOT NULL
    BEGIN
        UPDATE OrleansStorage
        SET
            PayloadBinary = @PayloadBinary,
            ModifiedOn = GETUTCDATE(),
            Version = Version % 2147483647 + 1,
            @NewGrainStateVersion = Version % 2147483647 + 1,
            @GrainStateVersion = Version % 2147483647 + 1
        WHERE
            GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
            AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
            AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
            AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
            AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
            AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
            OPTION(FAST 1, OPTIMIZE FOR(@GrainIdHash UNKNOWN, @GrainTypeHash UNKNOWN));
    END

    -- The grain state has not been read. The following locks rather pessimistically
    -- to ensure only one INSERT succeeds.
    IF @GrainStateVersion IS NULL
    BEGIN
        INSERT INTO OrleansStorage
        (
            GrainIdHash,
            GrainIdN0,
            GrainIdN1,
            GrainTypeHash,
            GrainTypeString,
            GrainIdExtensionString,
            ServiceId,
            PayloadBinary,
            ModifiedOn,
            Version
        )
        SELECT
            @GrainIdHash,
            @GrainIdN0,
            @GrainIdN1,
            @GrainTypeHash,
            @GrainTypeString,
            @GrainIdExtensionString,
            @ServiceId,
            @PayloadBinary,
            GETUTCDATE(),
            1
         WHERE NOT EXISTS
         (
            -- There should not be any version of this grain state.
            SELECT 1
            FROM OrleansStorage WITH(XLOCK, ROWLOCK, HOLDLOCK, INDEX(IX_OrleansStorage))
            WHERE
                GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
                AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
                AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
                AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
                AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
                AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
         ) OPTION(FAST 1, OPTIMIZE FOR(@GrainIdHash UNKNOWN, @GrainTypeHash UNKNOWN));

        IF @@ROWCOUNT > 0
        BEGIN
            SET @NewGrainStateVersion = 1;
        END
    END

    SELECT @NewGrainStateVersion AS NewGrainStateVersion;
    COMMIT TRANSACTION;' AS QueryText

UNION ALL
SELECT
    'ClearStorageKey',
    'BEGIN TRANSACTION;
    SET XACT_ABORT, NOCOUNT ON;
    DECLARE @NewGrainStateVersion AS INT = @GrainStateVersion;
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        ModifiedOn = GETUTCDATE(),
        Version = Version % 2147483647 + 1,
        @NewGrainStateVersion = Version % 2147483647 + 1
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
        AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
        AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
        OPTION(FAST 1, OPTIMIZE FOR(@GrainIdHash UNKNOWN, @GrainTypeHash UNKNOWN));

    SELECT @NewGrainStateVersion;
    COMMIT TRANSACTION;'

UNION ALL
SELECT
    'ReadFromStorageKey',
    '-- The application code will deserialize the relevant result. Not that the query optimizer
    -- estimates the result of rows based on its knowledge on the index. It does not know there
    -- will be only one row returned. Forcing the optimizer to process the first found row quickly
    -- creates an estimate for a one-row result and makes a difference on multi-million row tables.
    -- Also the optimizer is instructed to always use the same plan via index using the OPTIMIZE
    -- FOR UNKNOWN flags. These hints are only available in SQL Server 2008 and later. They
    -- should guarantee the execution time is robustly basically the same from query-to-query.
    SELECT
        PayloadBinary,
        Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
        AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
        AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        OPTION(FAST 1, OPTIMIZE FOR(@GrainIdHash UNKNOWN, @GrainTypeHash UNKNOWN));'

UNION ALL
SELECT
    'DeleteStorageKey',
    'BEGIN TRANSACTION;
    SET XACT_ABORT, NOCOUNT ON;
    DELETE FROM OrleansStorage OUTPUT DELETED.Version % 2147483647 + 1
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND (GrainIdN0 = @GrainIdN0 OR @GrainIdN0 IS NULL)
        AND (GrainIdN1 = @GrainIdN1 OR @GrainIdN1 IS NULL)
        AND (GrainTypeString = @GrainTypeString OR @GrainTypeString IS NULL)
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
        OPTION(FAST 1, OPTIMIZE FOR(@GrainIdHash UNKNOWN, @GrainTypeHash UNKNOWN));

    COMMIT TRANSACTION;'
) AS source
ON target.QueryKey = source.QueryKey
WHEN MATCHED THEN UPDATE SET QueryText = source.QueryText
WHEN NOT MATCHED THEN INSERT (QueryKey, QueryText) VALUES (source.QueryKey, source.QueryText);

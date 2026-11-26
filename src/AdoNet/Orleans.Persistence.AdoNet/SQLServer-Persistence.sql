-- 在共享主脚本创建 OrleansQuery后应用此脚本设置和重复升级。
-- 在应用前停止使用此数据库的所有 silos , 然后重新启动以重新装入查询 。
--
-- 本表的设计标准是:
--
-- 1. 含有任意内容,其序号为二进制、XML或 JSON。
-- 支持,以便人们能够利用储存处理能力
-- 必要时,这些类型不应产生额外的储存费用。
--
-- 2. 表格设计的规模应达到几十或数百(甚至更多)类型的设想
-- 联 年 月 日 月 日 月 月 日 grains 可能与甚至几十万个 grain 每一
-- 类型 grain。
--
-- 3. 表格及其相关业务应保持稳定。
-- 造成业务意外延误的结构性结构原因,应有可能
-- 以合理的快速度插入数据,不引起资源争议。
--
-- 4. 由于2和3中的原因,指数应尽可能缩小范围,以便与
-- 并且如果需要维护, 资源并不密集。 为此
-- 指数因设计而缩小(估计非集群)。
-- 以 grain 类型及其ID在储存中识别,这些 Orleans silo类型和ID是独有的。
-- ID是 grain ID 字节(如果字符串类型UTF-8 字节)和可能的扩展密钥 UTF-8
-- 与身份识别符号连接的字节,然后散列。
--
-- 散列原因:数据库引擎通常限制列长度,
-- 即使在限制范围内,也
-- 指数会厚,消耗更多的内存。
--
-- 在目前设置的设置中,ID和该类型被散成两个INT型式的情况,
-- 设定复合索引。当没有碰撞时,该索引可以快速定位
-- 与散列索引值一起, NVARCHAR(nnn) 值也
-- 存储后,它们被用来将散列碰撞处理成仅一个结果行。
--
-- 5. 设计设计导致储存重复,有理由认为会
-- 具有特定服务标识的服务数量少,在任何特定时间都可运行。
-- 与 grain ID的数量相比,不同类型身份证的数目相对较少
-- grain. 捕获量是,如果这些数据被分离到另一个表格中,它就会使INSTERT
-- 以及最新业务复杂,需要合并、临时变数和额外
-- 或一些组合的指数来使其发挥作用。 它看起来像是合适的策略
-- 可以是使用压缩表格。
--
-- 6. 出于上述原因, grain 州DDELETE将设置数据字段的NULL
-- 并通常更新版本号。这将减轻索引或索引的需要。
-- 统计维持数据,损失一些储存空间。
-- 单独维修操作。
--
-- 7. 在储存业务中,各栏的查询顺序必须完全相同
-- 自存储表操作支持可选流以来。
--
-- 8. 下一份版本的封号从214748364747回折到1,因此经常写 grain ,从不溢出
-- 32位数版本。 Orleans 比较版本时,只对等值进行比较,因此版本必须更改
-- 每一个写入都不需要永久增加, 但不需要永远增加。 版本的旧文本只能在
-- 2147483647 进一步写到同一份 grain。
IF OBJECT_ID(N'[OrleansStorage]', 'U') IS NULL
BEGIN
CREATE TABLE OrleansStorage
(
    -- 这些是用来保存书本的 Orleans 计算
    -- 这些人的( RelationalStorageProvide ),执行))、
    -- 标记为32位整数,这些整数被映射到 *散列字段。
    -- 绘图在代码中完成
    -- *字符串列包含相应的清晰名称字段。
    --
    -- 如果存在重复,则使用 GrainIdN0解决,
    -- GrainIdN1、 GrainIdExtensionString 和 GrainTypeString 字段。
    -- 假定这些是很少需要的。
    GrainIdHash                INT NOT NULL,
    GrainIdN0                BIGINT NOT NULL,
    GrainIdN1                BIGINT NOT NULL,
    GrainTypeHash            INT NOT NULL,
    GrainTypeString            NVARCHAR(512) NOT NULL,
    GrainIdExtensionString    NVARCHAR(512) NULL,
    ServiceId                NVARCHAR(150) NOT NULL,

    -- 有效载荷
    PayloadBinary    VARBINARY(MAX) NULL,

    -- 信息字段,没有其他用途。
    ModifiedOn DATETIME2(3) NOT NULL,

    -- 储存的有效载荷的版本。
    Version INT NULL

    -- 以下原则上是首要关键,但太厚了
    -- 需要索引,因此数值被仓散,只有碰撞才能解决
    -- ,即,在索引查询确定右侧
    -- 下至 [0, n] 相关行,n 指相对相撞价值对数。
);

IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_OrleansStorage' AND object_id = OBJECT_ID('OrleansStorage'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_OrleansStorage ON OrleansStorage(GrainIdHash, GrainTypeHash);
END

-- 这确保了锁升级不会锁定整个桌子,因为整桌可能非常庞大。
-- 见 https://www.littlekendra.com/2016/02/04/why-rowlock-hints-can-make-queries-slower-and-blocking-worse-in-sql-server/。
ALTER TABLE OrleansStorage SET(LOCK_ESCALATION = DISABLE);

-- 带有 ID 的特性是压缩。如果支持,它用于 OrleansStorage 表。这是一个 Enterprises 特性。
-- 这消耗了更多的处理周期,但应该节省 GrainIdString、 GrainTypeString 和 ServiceId的空间,而
-- 有效载荷将压缩。
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

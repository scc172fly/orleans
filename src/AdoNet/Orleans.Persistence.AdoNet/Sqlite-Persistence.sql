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
CREATE TABLE IF NOT EXISTS OrleansStorage
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
    PayloadBinary    BLOB NULL,
    -- 仅供参考的字段，无其他用途。
    ModifiedOn DATETIME NOT NULL,
    -- 已存储有效载荷的版本。
    Version INT NULL
    -- 以下字段原则上应构成主键，但作为索引会过宽，
    -- 因此对其值进行哈希，仅在发生哈希冲突时才
    -- 使用这些字段区分发生哈希冲突的记录。也就是说，在索引查询将匹配的行
    -- 缩小到 [0, n] 后（n 为发生冲突的值对数量），再使用这些字段。
);

CREATE INDEX IF NOT EXISTS IX_OrleansStorage ON OrleansStorage(GrainIdHash, GrainTypeHash);


-- 使用乐观并发控制更新现有 Grain 状态；若状态不存在，则插入。
INSERT INTO OrleansQuery (QueryKey, QueryText) VALUES 
('WriteToStorageKey', '
    -- TEMP objects survive pooled connection reuse. Bump the suffix on both object names whenever the trigger body changes.
    CREATE TEMP TABLE IF NOT EXISTS OrleansStorageWriteRequest_11_0
    (
        GrainIdHash INT NOT NULL,
        GrainIdN0 BIGINT NOT NULL,
        GrainIdN1 BIGINT NOT NULL,
        GrainTypeHash INT NOT NULL,
        GrainTypeString NVARCHAR(512) NOT NULL,
        GrainIdExtensionString NVARCHAR(512) NULL,
        ServiceId NVARCHAR(150) NOT NULL,
        PayloadBinary BLOB NULL,
        GrainStateVersion INT NULL,
        Applied INT NOT NULL DEFAULT 0
    );

    CREATE TEMP TRIGGER IF NOT EXISTS OrleansStorageWriteApply_11_0
    AFTER INSERT ON OrleansStorageWriteRequest_11_0
    BEGIN
        UPDATE OrleansStorage
        SET
            PayloadBinary = NEW.PayloadBinary,
            ModifiedOn = datetime(''now''),
            Version = Version % 2147483647 + 1
        WHERE
            GrainIdHash = NEW.GrainIdHash AND GrainTypeHash = NEW.GrainTypeHash
            AND GrainIdN0 = NEW.GrainIdN0 AND GrainIdN1 = NEW.GrainIdN1
            AND GrainTypeString = NEW.GrainTypeString
            AND (GrainIdExtensionString = NEW.GrainIdExtensionString OR (GrainIdExtensionString IS NULL AND NEW.GrainIdExtensionString IS NULL))
            AND ServiceId = NEW.ServiceId
            AND Version = NEW.GrainStateVersion;

        UPDATE OrleansStorageWriteRequest_11_0 SET Applied = changes() WHERE rowid = NEW.rowid;

        INSERT INTO OrleansStorage (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
        SELECT NEW.GrainIdHash, NEW.GrainIdN0, NEW.GrainIdN1, NEW.GrainTypeHash, NEW.GrainTypeString, NEW.GrainIdExtensionString, NEW.ServiceId, NEW.PayloadBinary, datetime(''now''), 1
        WHERE NEW.GrainStateVersion IS NULL
          AND NOT EXISTS (
            SELECT 1 FROM OrleansStorage
            WHERE GrainIdHash = NEW.GrainIdHash AND GrainTypeHash = NEW.GrainTypeHash
            AND GrainIdN0 = NEW.GrainIdN0 AND GrainIdN1 = NEW.GrainIdN1
            AND GrainTypeString = NEW.GrainTypeString
            AND (GrainIdExtensionString = NEW.GrainIdExtensionString OR (GrainIdExtensionString IS NULL AND NEW.GrainIdExtensionString IS NULL))
            AND ServiceId = NEW.ServiceId
        );

        UPDATE OrleansStorageWriteRequest_11_0 SET Applied = Applied + changes(), PayloadBinary = NULL WHERE rowid = NEW.rowid;
    END;

    DELETE FROM OrleansStorageWriteRequest_11_0;

    INSERT INTO OrleansStorageWriteRequest_11_0 (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId, PayloadBinary, GrainStateVersion)
    VALUES (@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @PayloadBinary, @GrainStateVersion);

    SELECT CASE WHEN @GrainStateVersion IS NULL THEN 1 ELSE @GrainStateVersion % 2147483647 + 1 END AS NewGrainStateVersion
    FROM OrleansStorageWriteRequest_11_0
    WHERE Applied > 0;

    SELECT @GrainStateVersion AS NewGrainStateVersion
    FROM OrleansStorageWriteRequest_11_0
    WHERE Applied = 0
        AND @GrainStateVersion IS NOT NULL;
')
ON CONFLICT(QueryKey) DO UPDATE SET QueryText = excluded.QueryText;

-- 读取指定 Grain 状态的二进制有效载荷及当前版本。
INSERT INTO OrleansQuery (QueryKey, QueryText) VALUES 
('ReadFromStorageKey', '
    SELECT
        PayloadBinary,
        Version AS Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash
        AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1
        AND GrainTypeString = @GrainTypeString
        AND (GrainIdExtensionString = @GrainIdExtensionString OR (GrainIdExtensionString IS NULL AND @GrainIdExtensionString IS NULL))
        AND ServiceId = @ServiceId
    LIMIT 1;
')
ON CONFLICT(QueryKey) DO UPDATE SET QueryText = excluded.QueryText;

-- 通过将有效载荷设为 null 并递增版本号来清除 Grain 状态，以保持一致性。
INSERT INTO OrleansQuery (QueryKey, QueryText) VALUES 
('ClearStorageKey', '
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        ModifiedOn = datetime(''now''),
        Version = Version % 2147483647 + 1
    WHERE
        GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash
        AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1
        AND GrainTypeString = @GrainTypeString
        AND (GrainIdExtensionString = @GrainIdExtensionString OR (GrainIdExtensionString IS NULL AND @GrainIdExtensionString IS NULL))
        AND ServiceId = @ServiceId
        AND Version = @GrainStateVersion;

    SELECT @GrainStateVersion % 2147483647 + 1 AS NewGrainStateVersion
    WHERE changes() > 0;

    SELECT @GrainStateVersion AS NewGrainStateVersion
    WHERE changes() = 0
        AND @GrainStateVersion IS NOT NULL;
')
ON CONFLICT(QueryKey) DO UPDATE SET QueryText = excluded.QueryText;

-- 将超出范围的版本号规范化，同时保留其有效载荷。
UPDATE OrleansStorage
SET Version = (Version - 1) % 2147483647 + 1
WHERE Version > 2147483647;

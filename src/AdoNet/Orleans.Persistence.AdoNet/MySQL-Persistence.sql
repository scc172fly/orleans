-- 共享的 Main 脚本创建 OrleansQuery 后，应用此脚本以执行初始化及可重复升级。
-- 应用脚本前，停止所有使用此数据库的 Silo，之后重新启动以重新加载查询。
-- 重新创建存储过程后，重新授予各例程专用的 EXECUTE 权限。
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
    Version INT NULL,
    INDEX IX_OrleansStorage (GrainIdHash, GrainTypeHash)

    -- 以下字段原则上应构成主键，但作为索引会过宽，
    -- 因此对其值进行哈希，仅在发生哈希冲突时才
    -- 使用这些字段区分发生哈希冲突的记录。也就是说，在索引查询将匹配的行
    -- 缩小到 [0, n] 后（n 为发生冲突的值对数量），再使用这些字段。
) ROW_FORMAT = COMPRESSED KEY_BLOCK_SIZE = 16;

DROP PROCEDURE IF EXISTS ClearStorage;
DROP PROCEDURE IF EXISTS DeleteStorage;
DROP PROCEDURE IF EXISTS WriteToStorage;

DELIMITER $$

CREATE PROCEDURE ClearStorage
(
    in _GrainIdHash INT,
    in _GrainIdN0 BIGINT,
    in _GrainIdN1 BIGINT,
    in _GrainTypeHash INT,
    in _GrainTypeString NVARCHAR(512),
    in _GrainIdExtensionString NVARCHAR(512),
    in _ServiceId NVARCHAR(150),
    in _GrainStateVersion INT
)
BEGIN
    DECLARE _newGrainStateVersion INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    DECLARE EXIT HANDLER FOR SQLWARNING BEGIN ROLLBACK; RESIGNAL; END;

    SET _newGrainStateVersion = _GrainStateVersion;

    -- 默认隔离级别为 REPEATABLE READ，可能导致间隙锁问题。
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        Version = Version % 2147483647 + 1
    WHERE
        GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
        AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
        AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
        AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
        AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
        LIMIT 1;

    IF ROW_COUNT() > 0
    THEN
        SET _newGrainStateVersion = _GrainStateVersion % 2147483647 + 1;
    END IF;

    SELECT _newGrainStateVersion AS NewGrainStateVersion;
    COMMIT;
END$$

CREATE PROCEDURE DeleteStorage
(
    in _GrainIdHash INT,
    in _GrainIdN0 BIGINT,
    in _GrainIdN1 BIGINT,
    in _GrainTypeHash INT,
    in _GrainTypeString NVARCHAR(512),
    in _GrainIdExtensionString NVARCHAR(512),
    in _ServiceId NVARCHAR(150),
    in _GrainStateVersion INT
)
BEGIN
    DECLARE _newGrainStateVersion INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    DECLARE EXIT HANDLER FOR SQLWARNING BEGIN ROLLBACK; RESIGNAL; END;

    SET _newGrainStateVersion = _GrainStateVersion;

    -- 默认隔离级别为 REPEATABLE READ，可能导致间隙锁问题。
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;
    DELETE FROM OrleansStorage
    WHERE
        GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
        AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
        AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
        AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
        AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
        LIMIT 1;

    IF ROW_COUNT() > 0
    THEN
        SET _newGrainStateVersion = _GrainStateVersion % 2147483647 + 1;
    END IF;

    SELECT _newGrainStateVersion AS NewGrainStateVersion;
    COMMIT;
END$$

DELIMITER $$
CREATE PROCEDURE WriteToStorage
(
    in _GrainIdHash INT,
    in _GrainIdN0 BIGINT,
    in _GrainIdN1 BIGINT,
    in _GrainTypeHash INT,
    in _GrainTypeString NVARCHAR(512),
    in _GrainIdExtensionString NVARCHAR(512),
    in _ServiceId NVARCHAR(150),
    in _GrainStateVersion INT,
    in _PayloadBinary BLOB
)
BEGIN
    DECLARE _newGrainStateVersion INT;
    DECLARE _rowCount INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    DECLARE EXIT HANDLER FOR SQLWARNING BEGIN ROLLBACK; RESIGNAL; END;

    SET _newGrainStateVersion = _GrainStateVersion;

    -- 默认隔离级别为 REPEATABLE READ，可能导致间隙锁问题。
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;

    -- Grain 状态不为 null，因此必定已从存储中读取过该状态。
    -- 尝试更新该状态。
    --
    -- 当 Orleans 正常运行且未发生脑裂时，
    -- 给定 ID 和类型组合仅对应一个 Grain。
    -- 若 Orleans 的保证成立，该 Grain 基本上会串行保存状态。即使
    -- 不满足这一点，版本号也能确保更新正确执行。
    --
    -- 发生脑裂时，可能会出现两个或更多具有相同 ID 和类型组合的
    -- Grain。当它们尝试并发执行 INSERT 时，
    -- 必须先对表采用悲观锁，以确保其中一个
    -- Grain 收到返回值 @GrainStateVersion = 1，而其他 Grain 无法
    -- 更新存储。以下设计旨在减少正常运行时的锁定。
    --
    -- 如果显式返回的版本号仍未改变，Orleans 将其解释为更新失败，
    -- 并抛出 InconsistentStateException。
    --
    -- 更多信息见 https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence。
    IF _GrainStateVersion IS NOT NULL
    THEN
        UPDATE OrleansStorage
        SET
            PayloadBinary = _PayloadBinary,
            ModifiedOn = UTC_TIMESTAMP(),
            Version = Version % 2147483647 + 1
        WHERE
            GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
            LIMIT 1;

        IF ROW_COUNT() > 0
        THEN
            SET _newGrainStateVersion = _GrainStateVersion % 2147483647 + 1;
            SET _GrainStateVersion = _newGrainStateVersion;
        END IF;
    END IF;

    -- 尚未读取 Grain 状态。以下操作采用较严格的悲观锁策略，
    -- 以确保只有一个 INSERT 成功。
    IF _GrainStateVersion IS NULL
    THEN
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
        SELECT * FROM ( SELECT
            _GrainIdHash,
            _GrainIdN0,
            _GrainIdN1,
            _GrainTypeHash,
            _GrainTypeString,
            _GrainIdExtensionString,
            _ServiceId,
            _PayloadBinary,
            UTC_TIMESTAMP(),
            1) AS TMP
        WHERE NOT EXISTS
        (
            -- 此 Grain 状态不应已有任何版本。
            SELECT 1
            FROM OrleansStorage
            WHERE
                GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
                AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
                AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
                AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
                AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
                AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        ) LIMIT 1;

        IF ROW_COUNT() > 0
        THEN
            SET _newGrainStateVersion = 1;
        END IF;
    END IF;

    SELECT _newGrainStateVersion AS NewGrainStateVersion;
    COMMIT;
END$$

DELIMITER ;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadFromStorageKey',
    'SELECT
        PayloadBinary,
        UTC_TIMESTAMP(),
        Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        LIMIT 1;'
)
ON DUPLICATE KEY UPDATE QueryText = VALUES(QueryText);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'WriteToStorageKey','
    call WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);'
)
ON DUPLICATE KEY UPDATE QueryText = VALUES(QueryText);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ClearStorageKey','
    call ClearStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion);'
)
ON DUPLICATE KEY UPDATE QueryText = VALUES(QueryText);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'DeleteStorageKey','
    call DeleteStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion);'
)
ON DUPLICATE KEY UPDATE QueryText = VALUES(QueryText);

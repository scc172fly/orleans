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
DECLARE
    objectCount NUMBER;
BEGIN
    SELECT COUNT(*) INTO objectCount
    FROM ALL_TABLES
    WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND TABLE_NAME = 'ORLEANSSTORAGE';
    IF objectCount = 0 THEN
        EXECUTE IMMEDIATE q'[CREATE TABLE "ORLEANSSTORAGE"
(

    -- These are for the book keeping. Orleans calculates
    -- these hashes (see RelationalStorageProvide implementation),
    -- which are signed 32 bit integers mapped to the *Hash fields.
    -- The mapping is done in the code. The
    -- *String columns contain the corresponding clear name fields.
    --
    -- If there are duplicates, they are resolved by using GrainIdN0,
    -- GrainIdN1, GrainIdExtensionString and GrainTypeString fields.
    -- It is assumed these would be rarely needed.
    "GRAINIDHASH" NUMBER(*,0) NOT NULL ENABLE,
    "GRAINIDN0" NUMBER(19,0) NOT NULL ENABLE,
    "GRAINIDN1" NUMBER(19,0) NOT NULL ENABLE,
    "GRAINTYPEHASH" NUMBER(*,0) NOT NULL ENABLE,
    "GRAINTYPESTRING" NVARCHAR2(512) NOT NULL ENABLE,
    "GRAINIDEXTENSIONSTRING" NVARCHAR2(512),
    "SERVICEID" NVARCHAR2(150) NOT NULL ENABLE,

    -- Payload
    "PAYLOADBINARY" BLOB,

    -- Informational field, no other use.
    "MODIFIEDON" TIMESTAMP (6) NOT NULL ENABLE,
    -- The version of the stored payload.
    "VERSION" NUMBER(*,0)

    -- The following would in principle be the primary key, but it would be too thick
    -- to be indexed, so the values are hashed and only collisions will be solved
    -- by using the fields. That is, after the indexed queries have pinpointed the right
    -- rows down to [0, n] relevant ones, n being the number of collided value pairs.
)]';
    END IF;

    SELECT COUNT(*) INTO objectCount
    FROM ALL_INDEXES
    WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND INDEX_NAME = 'IX_ORLEANSSTORAGE';
    IF objectCount = 0 THEN
        EXECUTE IMMEDIATE 'CREATE INDEX "IX_ORLEANSSTORAGE" ON "ORLEANSSTORAGE" ("GRAINIDHASH", "GRAINTYPEHASH") PARALLEL COMPRESS';
    END IF;
END;
/

CREATE OR REPLACE FUNCTION WriteToStorage(PARAM_GRAINIDHASH IN NUMBER, PARAM_GRAINIDN0 IN NUMBER, PARAM_GRAINIDN1 IN NUMBER, PARAM_GRAINTYPEHASH IN NUMBER, PARAM_GRAINTYPESTRING IN NVARCHAR2,
                                             PARAM_GRAINIDEXTENSIONSTRING IN NVARCHAR2, PARAM_SERVICEID IN VARCHAR2, PARAM_GRAINSTATEVERSION IN NUMBER, PARAM_PAYLOADBINARY IN BLOB)
  RETURN NUMBER IS
  rowcount NUMBER;
  newGrainStateVersion NUMBER := PARAM_GRAINSTATEVERSION;
  PRAGMA AUTONOMOUS_TRANSACTION;
  BEGIN
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


    -- 如果 @GrainStateVersion 不为零，此分支假设该状态在数据库中已存在。
    -- 当状态是新建的时，Orleans 提供 NULL 值。
    IF newGrainStateVersion IS NOT NULL THEN
        UPDATE OrleansStorage
        SET
            PayloadBinary = PARAM_PAYLOADBINARY,
            ModifiedOn = sys_extract_utc(systimestamp),
            Version = MOD(Version, 2147483647) + 1
        WHERE
            GrainIdHash = PARAM_GRAINIDHASH AND PARAM_GRAINIDHASH IS NOT NULL
            AND GrainTypeHash = PARAM_GRAINTYPEHASH AND PARAM_GRAINTYPEHASH IS NOT NULL
            AND (GrainIdN0 = PARAM_GRAINIDN0 OR PARAM_GRAINIDN0 IS NULL)
            AND (GrainIdN1 = PARAM_GRAINIDN1 OR PARAM_GRAINIDN1 IS NULL)
            AND (GrainTypeString = PARAM_GRAINTYPESTRING OR PARAM_GRAINTYPESTRING IS NULL)
            AND ((PARAM_GRAINIDEXTENSIONSTRING IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = PARAM_GRAINIDEXTENSIONSTRING) OR PARAM_GRAINIDEXTENSIONSTRING IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = PARAM_SERVICEID AND PARAM_SERVICEID IS NOT NULL
            AND Version IS NOT NULL AND Version = PARAM_GRAINSTATEVERSION AND PARAM_GRAINSTATEVERSION IS NOT NULL
    RETURNING Version INTO newGrainStateVersion;

    rowcount := SQL%ROWCOUNT;

    IF rowcount = 1 THEN
      COMMIT;
      RETURN(newGrainStateVersion);
    END IF;
    END IF;

    -- 尚未读取 Grain 状态。以下操作采用较严格的悲观锁策略，
    -- 以确保只有一个 INSERT 成功。
    IF PARAM_GRAINSTATEVERSION IS NULL THEN
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
            PARAM_GRAINIDHASH,
            PARAM_GRAINIDN0,
            PARAM_GRAINIDN1,
            PARAM_GRAINTYPEHASH,
            PARAM_GRAINTYPESTRING,
            PARAM_GRAINIDEXTENSIONSTRING,
            PARAM_SERVICEID,
            PARAM_PAYLOADBINARY,
            sys_extract_utc(systimestamp),
            1 FROM DUAL
         WHERE NOT EXISTS
         (
            -- 此 Grain 状态不应已有任何版本。
            SELECT 1
            FROM OrleansStorage
            WHERE
                GrainIdHash = PARAM_GRAINIDHASH AND PARAM_GRAINIDHASH IS NOT NULL
                AND GrainTypeHash = PARAM_GRAINTYPEHASH AND PARAM_GRAINTYPEHASH IS NOT NULL
                AND (GrainIdN0 = PARAM_GRAINIDN0 OR PARAM_GRAINIDN0 IS NULL)
                AND (GrainIdN1 = PARAM_GRAINIDN1 OR PARAM_GRAINIDN1 IS NULL)
                AND (GrainTypeString = PARAM_GRAINTYPESTRING OR PARAM_GRAINTYPESTRING IS NULL)
                AND ((PARAM_GRAINIDEXTENSIONSTRING IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = PARAM_GRAINIDEXTENSIONSTRING) OR PARAM_GRAINIDEXTENSIONSTRING IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = PARAM_SERVICEID AND PARAM_SERVICEID IS NOT NULL
         );

     rowCount := SQL%ROWCOUNT;

        IF rowCount > 0 THEN
            newGrainStateVersion := 1;
        END IF;
    END IF;
  COMMIT;
    RETURN(newGrainStateVersion);
  END;
/

CREATE OR REPLACE FUNCTION ClearStorage(PARAM_GRAINIDHASH IN NUMBER, PARAM_GRAINIDN0 IN NUMBER, PARAM_GRAINIDN1 IN NUMBER, PARAM_GRAINTYPEHASH IN NUMBER, PARAM_GRAINTYPESTRING IN NVARCHAR2,
                                             PARAM_GRAINIDEXTENSIONSTRING IN NVARCHAR2, PARAM_SERVICEID IN VARCHAR2, PARAM_GRAINSTATEVERSION IN NUMBER)
  RETURN NUMBER IS
  rowcount NUMBER;
  newGrainStateVersion NUMBER := PARAM_GRAINSTATEVERSION;
  PRAGMA AUTONOMOUS_TRANSACTION;
  BEGIN
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        ModifiedOn = sys_extract_utc(systimestamp),
        Version = MOD(Version, 2147483647) + 1
    WHERE GrainIdHash = PARAM_GRAINIDHASH AND PARAM_GRAINIDHASH IS NOT NULL
      AND GrainTypeHash = PARAM_GRAINTYPEHASH AND PARAM_GRAINTYPEHASH IS NOT NULL
      AND (GrainIdN0 = PARAM_GRAINIDN0 OR PARAM_GRAINIDN0 IS NULL)
      AND (GrainIdN1  = PARAM_GRAINIDN1 OR PARAM_GRAINIDN1 IS NULL)
      AND (GrainTypeString = PARAM_GRAINTYPESTRING OR PARAM_GRAINTYPESTRING IS NULL)
      AND ((PARAM_GRAINIDEXTENSIONSTRING IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = PARAM_GRAINIDEXTENSIONSTRING) OR PARAM_GRAINIDEXTENSIONSTRING IS NULL AND GrainIdExtensionString IS NULL)
      AND ServiceId = PARAM_SERVICEID AND PARAM_SERVICEID IS NOT NULL
      AND Version IS NOT NULL AND Version = PARAM_GRAINSTATEVERSION AND PARAM_GRAINSTATEVERSION IS NOT NULL
    RETURNING Version INTO newGrainStateVersion;

    COMMIT;
    RETURN(newGrainStateVersion);
  END;
/

CREATE OR REPLACE FUNCTION DeleteStorage(PARAM_GRAINIDHASH IN NUMBER, PARAM_GRAINIDN0 IN NUMBER, PARAM_GRAINIDN1 IN NUMBER, PARAM_GRAINTYPEHASH IN NUMBER, PARAM_GRAINTYPESTRING IN NVARCHAR2,
                                             PARAM_GRAINIDEXTENSIONSTRING IN NVARCHAR2, PARAM_SERVICEID IN VARCHAR2, PARAM_GRAINSTATEVERSION IN NUMBER)
  RETURN NUMBER IS
  rowcount NUMBER;
  newGrainStateVersion NUMBER := PARAM_GRAINSTATEVERSION;
  PRAGMA AUTONOMOUS_TRANSACTION;
  BEGIN
    DELETE FROM OrleansStorage
    WHERE GrainIdHash = PARAM_GRAINIDHASH AND PARAM_GRAINIDHASH IS NOT NULL
      AND GrainTypeHash = PARAM_GRAINTYPEHASH AND PARAM_GRAINTYPEHASH IS NOT NULL
      AND (GrainIdN0 = PARAM_GRAINIDN0 OR PARAM_GRAINIDN0 IS NULL)
      AND (GrainIdN1  = PARAM_GRAINIDN1 OR PARAM_GRAINIDN1 IS NULL)
      AND (GrainTypeString = PARAM_GRAINTYPESTRING OR PARAM_GRAINTYPESTRING IS NULL)
      AND ((PARAM_GRAINIDEXTENSIONSTRING IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = PARAM_GRAINIDEXTENSIONSTRING) OR PARAM_GRAINIDEXTENSIONSTRING IS NULL AND GrainIdExtensionString IS NULL)
      AND ServiceId = PARAM_SERVICEID AND PARAM_SERVICEID IS NOT NULL
      AND Version IS NOT NULL AND Version = PARAM_GRAINSTATEVERSION AND PARAM_GRAINSTATEVERSION IS NOT NULL;

    rowCount := SQL%ROWCOUNT;

    IF rowCount > 0 THEN
        newGrainStateVersion := MOD(PARAM_GRAINSTATEVERSION, 2147483647) + 1;
    END IF;

    COMMIT;
    RETURN(newGrainStateVersion);
  END;
/

MERGE INTO OrleansQuery target
USING
(
    SELECT 'WriteToStorageKey' AS QueryKey,'
  SELECT WriteToStorage(:GrainIdHash, :GrainIdN0, :GrainIdN1, :GrainTypeHash, :GrainTypeString,
                                             :GrainIdExtensionString, :ServiceId, :GrainStateVersion, :PayloadBinary)
                                             AS NewGrainStateVersion FROM DUAL
' AS QueryText FROM DUAL

    UNION ALL
    SELECT 'ClearStorageKey',
    'SELECT ClearStorage(:GrainIdHash, :GrainIdN0, :GrainIdN1, :GrainTypeHash, :GrainTypeString,
                                             :GrainIdExtensionString, :ServiceId, :GrainStateVersion) AS Version FROM DUAL' FROM DUAL

    UNION ALL
    SELECT 'DeleteStorageKey',
    'SELECT DeleteStorage(:GrainIdHash, :GrainIdN0, :GrainIdN1, :GrainTypeHash, :GrainTypeString,
                                             :GrainIdExtensionString, :ServiceId, :GrainStateVersion) AS Version FROM DUAL' FROM DUAL

    UNION ALL
    SELECT 'ReadFromStorageKey',
    '
     SELECT PayloadBinary, Version
     FROM OrleansStorage
     WHERE GrainIdHash = :GrainIdHash AND :GrainIdHash IS NOT NULL
       AND (GrainIdN0 = :GrainIdN0 OR :GrainIdN0 IS NULL)
       AND (GrainIdN1 = :GrainIdN1 OR :GrainIdN1 IS NULL)
       AND GrainTypeHash = :GrainTypeHash AND :GrainTypeHash IS NOT NULL
       AND (GrainTypeString = :GrainTypeString OR :GrainTypeString IS NULL)
       AND ((:GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = :GrainIdExtensionString) OR :GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
       AND ServiceId = :ServiceId AND :ServiceId IS NOT NULL' FROM DUAL
) source
ON (target.QueryKey = source.QueryKey)
WHEN MATCHED THEN UPDATE SET target.QueryText = source.QueryText
WHEN NOT MATCHED THEN INSERT (QueryKey, QueryText) VALUES (source.QueryKey, source.QueryText);
/

-- 将超出范围的版本号规范化，同时保留其有效载荷。
UPDATE ORLEANSSTORAGE
SET VERSION = MOD(VERSION - 1, 2147483647) + 1
WHERE VERSION > 2147483647;
/

COMMIT;

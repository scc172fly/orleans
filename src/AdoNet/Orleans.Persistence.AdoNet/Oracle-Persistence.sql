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
    -- 当 Orleans 在正常状态下运行时,如果该状态不分裂,将
    -- 只有一个 grain ,只有给定的ID和类型组合。此
    -- grain 如果 Orleans 保证得到坚持,则主要以序列形式保存国家。
    -- 如果不是,由于版本号,更新应正确工作。
    --
    -- 在分裂的大脑情况下,可能出现两种或两种以上的情况
    -- 与给定的ID和类型组合grains 。当他们尝试 INSERT 时
    -- 同时,表格需要以悲观的方式锁在
    -- grains 得到 @GrainStateVersion = 1 回报,而另一个 grains 将失败
    -- 以更新存储。以下安排是为了减少正常操作中的锁定。
    --
    -- 如果明确返回的版本号仍然相同, Orleans 解释其内容,更新没有成功
    -- 抛下一箭, InconsistentStateException.
    --
    -- 见 https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence中的进一步资料。


    -- 如果 @GrainStateVersion 不是零,则该分支假定它存在于数据库中。
    -- 当状态是新时,NULL值由 Orleans 提供。
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

    -- grain 状态尚未读取。 以下锁的锁定相当悲观
    -- 确保只有一次国家清单专家审评组成功。
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
            -- 不应有任何版本的 grain 状态。
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

-- 将超大型版本正常化,同时保存其有效载荷。
UPDATE ORLEANSSTORAGE
SET VERSION = MOD(VERSION - 1, 2147483647) + 1
WHERE VERSION > 2147483647;
/

COMMIT;

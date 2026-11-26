-- 在共享主脚本创建 OrleansQuery后应用此脚本设置和重复升级。
-- 在应用前停止使用此数据库的所有 silos , 然后重新启动以重新装入查询 。
-- 在程序重新建立后,再使用常规专项执行赠款。
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
CREATE TABLE IF NOT EXISTS OrleansStorage
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
    PayloadBinary    BLOB NULL,

    -- 信息字段,没有其他用途。
    ModifiedOn DATETIME NOT NULL,

    -- 储存的有效载荷的版本。
    Version INT NULL,
    INDEX IX_OrleansStorage (GrainIdHash, GrainTypeHash)

    -- 以下原则上是首要关键,但太厚了
    -- 需要索引,因此数值被仓散,只有碰撞才能解决
    -- ,即,在索引查询确定右侧
    -- 下至 [0, n] 相关行,n 指相对相撞价值对数。
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

    -- 默认值是可重读的默认值, 可能导致差距锁定问题
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

    -- 默认值是可重读的默认值, 可能导致差距锁定问题
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

    -- 默认值是可重读的默认值, 可能导致差距锁定问题
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;

    -- Grain 状态并非无效, 因此该国必须已经读过之前存储处的文字 。
    -- 让我们试着更新它。
    --
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

    -- grain 状态尚未读取。 以下锁的锁定相当悲观
    -- 以确保只有INSTERT成功。
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
            -- 不应有任何版本的 grain 状态。
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

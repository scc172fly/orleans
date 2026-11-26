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
    Version INT NULL
    -- 以下原则上是首要关键,但太厚了
    -- 需要索引,因此数值被仓散,只有碰撞才能解决
    -- ,即,在索引查询确定右侧
    -- 下至 [0, n] 相关行,n 指相对相撞价值对数。
);

CREATE INDEX IF NOT EXISTS IX_OrleansStorage ON OrleansStorage(GrainIdHash, GrainTypeHash);


-- 更新现有 grain 状态,实行乐观的同值货币控制,如果没有这种控制,则插入。
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

-- 获取二进制有效载荷和当前版本的指定 grain 状态。
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

-- 通过将有效载荷设为无效并增加版本以保持一致性来清除 grain 状态。
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

-- 将超大型版本正常化,同时保存其有效载荷。
UPDATE OrleansStorage
SET Version = (Version - 1) % 2147483647 + 1
WHERE Version > 2147483647;

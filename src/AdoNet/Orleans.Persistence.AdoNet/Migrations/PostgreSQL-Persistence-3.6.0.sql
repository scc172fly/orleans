-- 运行此迁移以更新 PostgreSQL 持久性表和3.6.0之前创建的部署常规。

BEGIN;

-- 更改日期类型

ALTER TABLE OrleansStorage
ALTER COLUMN modifiedon TYPE TIMESTAMPTZ USING modifiedon AT TIME ZONE 'UTC';

-- 重建常规

CREATE OR REPLACE FUNCTION writetostorage(
    _grainidhash integer,
    _grainidn0 bigint,
    _grainidn1 bigint,
    _graintypehash integer,
    _graintypestring character varying,
    _grainidextensionstring character varying,
    _serviceid character varying,
    _grainstateversion integer,
    _payloadbinary bytea)
    RETURNS TABLE(newgrainstateversion integer)
    LANGUAGE 'plpgsql'
AS $function$
    DECLARE
     _newGrainStateVersion integer := _GrainStateVersion;
     RowCountVar integer := 0;

    BEGIN

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
            ModifiedOn = now(),
            Version = Version + 1

        WHERE
            GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0
        THEN
            _newGrainStateVersion := _GrainStateVersion + 1;
        END IF;
    END IF;

    -- grain 状态尚未读取。 以下锁的锁定相当悲观
    -- 确保只有一次国家清单专家审评组成功。
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
        SELECT
            _GrainIdHash,
            _GrainIdN0,
            _GrainIdN1,
            _GrainTypeHash,
            _GrainTypeString,
            _GrainIdExtensionString,
            _ServiceId,
            _PayloadBinary,
           now(),
            1
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
         );

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0
        THEN
            _newGrainStateVersion := 1;
        END IF;
    END IF;

    RETURN QUERY SELECT _newGrainStateVersion AS NewGrainStateVersion;
END

$function$;

COMMIT;

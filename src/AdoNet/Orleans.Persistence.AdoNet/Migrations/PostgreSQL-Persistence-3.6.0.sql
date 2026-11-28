-- 运行此迁移脚本，为 3.6.0 之前创建的部署升级 PostgreSQL 持久化表和例程。

BEGIN;

-- 更改日期时间类型。

ALTER TABLE OrleansStorage
ALTER COLUMN modifiedon TYPE TIMESTAMPTZ USING modifiedon AT TIME ZONE 'UTC';

-- 重新创建例程。

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

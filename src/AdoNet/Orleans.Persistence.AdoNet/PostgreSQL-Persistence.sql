-- 共享的 Main 脚本创建 OrleansQuery 后，应用此脚本以执行初始化及可重复升级。
-- 应用脚本前，停止所有使用此数据库的 Silo，之后重新启动以重新加载查询。
BEGIN;

CREATE TABLE IF NOT EXISTS OrleansStorage
(
    grainidhash integer NOT NULL,
    grainidn0 bigint NOT NULL,
    grainidn1 bigint NOT NULL,
    graintypehash integer NOT NULL,
    graintypestring character varying(512)  NOT NULL,
    grainidextensionstring character varying(512) ,
    serviceid character varying(150)  NOT NULL,
    payloadbinary bytea,
    modifiedon timestamptz NOT NULL,
    -- 版本号达到 2147483647 后，下一个值回绕至 1，因此频繁写入的 Grain 不会发生版本号溢出。
    -- Orleans 仅比较版本是否相等，因此每次写入时版本号必须变化，但不需要始终递增。
    -- 只有在对同一 Grain 又执行 2147483647 次写入后，旧版本号才可能再次匹配。
    version integer
);

CREATE INDEX IF NOT EXISTS ix_orleansstorage
    ON orleansstorage USING btree
    (grainidhash, graintypehash);

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
            Version = Version % 2147483647 + 1

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
            _newGrainStateVersion := _GrainStateVersion % 2147483647 + 1;
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

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'WriteToStorageKey','

        select * from WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);
')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadFromStorageKey','
    SELECT
        PayloadBinary,
        (now() at time zone ''utc''),
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
')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ClearStorageKey','
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        Version = Version % 2147483647 + 1
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
    Returning Version as NewGrainStateVersion
')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'DeleteStorageKey','
    DELETE FROM OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
    Returning Version % 2147483647 + 1 as NewGrainStateVersion
')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

COMMIT;

/*
Orleans Grain 目录。
此表存储集群中所有 Grain 的位置。

注意：
ClusterId、ProviderId 和 GrainId 的组合构成 OrleansGrainDirectory 表的主键。
这些列的总长度达到 PostgreSQL 索引允许的最大键长度（2704 字节）。
请勿增加这些列的长度，否则可能超过 PostgreSQL 的键长度限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 标识集群实例。 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 标识 Grain 目录提供程序。 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 存储文本形式的 Grain ID。 */
    GrainId VARCHAR(2404) NOT NULL,

    /* 存储 Grain 所在的 Silo 地址。 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 存储该 Grain 在其所在 Silo 中的激活实例 ID。 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 存储 Grain 加入目录的时间。 */
    CreatedOn TIMESTAMPTZ NOT NULL,

    /* 标识唯一的 Grain 激活实例。 */
    CONSTRAINT PK_OrleansGrainDirectory PRIMARY KEY
    (
        ClusterId,
        ProviderId,
        GrainId
    )
);

/* 注册新的 Grain 激活实例。 */
CREATE OR REPLACE FUNCTION RegisterGrainActivation(
    _ClusterId VARCHAR(150),
    _ProviderId VARCHAR(150),
    _GrainId VARCHAR(600),
    _SiloAddress VARCHAR(100),
    _ActivationId VARCHAR(100)
)
RETURNS TABLE
(
    ClusterId VARCHAR(150),
    ProviderId VARCHAR(150),
    GrainId VARCHAR(600),
    SiloAddress VARCHAR(100),
    ActivationId VARCHAR(100)
)
LANGUAGE plpgsql
AS $$
#VARIABLE_CONFLICT USE_COLUMN
DECLARE
    _Now TIMESTAMPTZ := NOW();
BEGIN

RETURN QUERY
INSERT INTO OrleansGrainDirectory
(
    ClusterId,
    ProviderId,
    GrainId,
    SiloAddress,
    ActivationId,
    CreatedOn
)
SELECT
    _ClusterId,
    _ProviderId,
    _GrainId,
    _SiloAddress,
    _ActivationId,
    _Now
ON CONFLICT (ClusterId, ProviderId, GrainId)
DO UPDATE SET
    ClusterId = _ClusterId,
    ProviderId = _ProviderId,
    GrainId = _GrainId
RETURNING
    ClusterId,
    ProviderId,
    GrainId,
    SiloAddress,
    ActivationId;

END;
$$;

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'RegisterGrainActivationKey',
	'START TRANSACTION; SELECT * FROM RegisterGrainActivation (@ClusterId, @ProviderId, @GrainId, @SiloAddress, @ActivationId); COMMIT;'
;

/* 注销现有的 Grain 激活实例。 */
CREATE OR REPLACE FUNCTION UnregisterGrainActivation(
    _ClusterId VARCHAR(150),
    _ProviderId VARCHAR(150),
    _GrainId VARCHAR(600),
    _ActivationId VARCHAR(100)
)
RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    _RowCount INT;
BEGIN

DELETE FROM OrleansGrainDirectory
WHERE ClusterId = _ClusterId
    AND ProviderId = _ProviderId
    AND GrainId = _GrainId
    AND ActivationId = _ActivationId;

GET DIAGNOSTICS _RowCount = ROW_COUNT;

RETURN _RowCount;

END;
$$;

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'UnregisterGrainActivationKey',
	'SELECT * FROM UnregisterGrainActivation (@ClusterId, @ProviderId, @GrainId, @ActivationId)'
;

/* 查找现有的 Grain 激活实例。 */
CREATE OR REPLACE FUNCTION LookupGrainActivation(
    _ClusterId VARCHAR(150),
    _ProviderId VARCHAR(150),
    _GrainId VARCHAR(600)
)
RETURNS TABLE
(
    ClusterId VARCHAR(150),
    ProviderId VARCHAR(150),
    GrainId VARCHAR(600),
    SiloAddress VARCHAR(100),
    ActivationId VARCHAR(100)
)
LANGUAGE plpgsql
AS $$
#VARIABLE_CONFLICT USE_COLUMN
BEGIN

RETURN QUERY
SELECT
    ClusterId,
    ProviderId,
    GrainId,
    SiloAddress,
    ActivationId
FROM
    OrleansGrainDirectory
WHERE
    ClusterId = _ClusterId
    AND ProviderId = _ProviderId
    AND GrainId = _GrainId;

END;
$$;

INSERT INTO OrleansQuery
(
    QueryKey,
    QueryText
)
SELECT
    'LookupGrainActivationKey',
    'SELECT * FROM LookupGrainActivation(@ClusterId, @ProviderId, @GrainId)'
;

/* 注销指定 Silo 中的所有 Grain 激活实例。 */
CREATE OR REPLACE FUNCTION UnregisterGrainActivations(
    _ClusterId VARCHAR(150),
    _ProviderId VARCHAR(150),
    _SiloAddresses TEXT
)
RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
    _RowCount INT;
BEGIN

DELETE FROM OrleansGrainDirectory
WHERE
    ClusterId = _ClusterId
    AND ProviderId = _ProviderId
    AND SiloAddress = ANY (string_to_array(_SiloAddresses, '|'));

GET DIAGNOSTICS _RowCount = ROW_COUNT;

RETURN _RowCount;

END;
$$;

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'UnregisterGrainActivationsKey',
	'SELECT * FROM UnregisterGrainActivations (@ClusterId, @ProviderId, @SiloAddresses)'
;

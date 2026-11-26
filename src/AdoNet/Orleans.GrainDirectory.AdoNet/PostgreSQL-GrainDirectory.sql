/*
Orleans Grain 目录。
此表格存储集中所有 grains 的位置。

注:
ClusterId、 ProviderId和 GrainId 的组合构成 OrleansGrainDirectory 表格的主要关键字。
这些列加起来达到 PostgreSQL 指数(2704字节)允许的最大关键字大小。
应注意不要增加这些栏目长度,因为其可能超过 PostgreSQL的关键尺寸限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 确定群集实例 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 指定 grain 目录提供者 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 以文本格式持有 grain id */
    GrainId VARCHAR(2404) NOT NULL,

    /* 握住 silo 地址 grain 位于 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 将激活id 保存在 silo 所在位置 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 持有将 grain 添加到目录的时间 */
    CreatedOn TIMESTAMPTZ NOT NULL,

    /* 指定一个独有的 grain 激活 */
    CONSTRAINT PK_OrleansGrainDirectory PRIMARY KEY
    (
        ClusterId,
        ProviderId,
        GrainId
    )
);

/* 注册新的 grain 激活 */
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

/* 取消现有 grain 激活的不登记 */
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

/* 查看已有的 grain 激活 */
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

/* 全部不登记者 grain 指定 silos */
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

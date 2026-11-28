/*
Orleans Grain 目录。
此表存储集群中所有 Grain 的位置。

注意：
ClusterId、ProviderId 和 GrainId 的组合构成 OrleansGrainDirectory 表的主键。
这些列的总长度达到 SQL Server 索引允许的最大键长度（900 字节）。
请勿增加这些列的长度，否则可能超过 SQL Server 的键长度限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 标识集群实例。 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 标识 Grain 目录提供程序。 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 存储文本形式的 Grain ID。 */
    GrainId VARCHAR(600) NOT NULL,

    /* 存储 Grain 所在的 Silo 地址。 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 存储该 Grain 在其所在 Silo 中的激活实例 ID。 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 存储 Grain 加入目录的时间。 */
    CreatedOn DATETIMEOFFSET(3) NOT NULL,

    /* 标识唯一的 Grain 激活实例。 */
    CONSTRAINT PK_OrleansGrainDirectory PRIMARY KEY CLUSTERED
    (
        ClusterId ASC,
        ProviderId ASC,
        GrainId ASC
    )
)
GO

/* 注册新的 Grain 激活实例。 */
CREATE PROCEDURE RegisterGrainActivation
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @GrainId VARCHAR(600),
    @SiloAddress VARCHAR(100),
    @ActivationId VARCHAR(100)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Now DATETIMEOFFSET(3) = CAST(SYSUTCDATETIME() AS DATETIMEOFFSET(3));

BEGIN TRANSACTION;

/* 如果条目存在，则获取该条目。 */
/* 同时预先对键范围加锁，以备需要添加新条目时使用。 */
/* 需要此锁以防止死锁。 */
DECLARE @Exists INT =
(
    SELECT
        1
    FROM
        OrleansGrainDirectory WITH (UPDLOCK, HOLDLOCK)
    WHERE
        ClusterId = @ClusterId
        AND ProviderId = @ProviderId
        AND GrainId = @GrainId
);

IF @Exists = 1
BEGIN

    /* 如果条目已存在，则可直接返回。 */
    SELECT
        ClusterId,
        ProviderId,
        GrainId,
        SiloAddress,
        ActivationId
    FROM
        OrleansGrainDirectory
    WHERE
        ClusterId = @ClusterId
        AND ProviderId = @ProviderId
        AND GrainId = @GrainId;

END
ELSE
BEGIN

    /* 如果条目尚不存在，则立即添加。 */
    INSERT INTO OrleansGrainDirectory
    (
        ClusterId,
        ProviderId,
        GrainId,
        SiloAddress,
        ActivationId,
        CreatedOn
    )
    OUTPUT
        INSERTED.ClusterId,
        INSERTED.ProviderId,
        INSERTED.GrainId,
        INSERTED.SiloAddress,
        INSERTED.ActivationId
    VALUES
    (
        @ClusterId,
        @ProviderId,
        @GrainId,
        @SiloAddress,
        @ActivationId,
        @Now
    );

END

COMMIT;

GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'RegisterGrainActivationKey',
	'EXECUTE RegisterGrainActivation @ClusterId = @ClusterId, @ProviderId = @ProviderId, @GrainId = @GrainId, @SiloAddress = @SiloAddress, @ActivationId = @ActivationId'
GO

/* 注销现有的 Grain 激活实例。 */
CREATE PROCEDURE UnregisterGrainActivation
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @GrainId VARCHAR(600),
    @ActivationId VARCHAR(100)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 如果条目存在，则删除该条目。 */
/* 预先锁定哈希索引页以防止死锁。 */
DELETE OrleansGrainDirectory
FROM OrleansGrainDirectory
WHERE
    ClusterId = @ClusterId
    AND ProviderId = @ProviderId
    AND GrainId = @GrainId
    AND ActivationId = @ActivationId;

SELECT @@ROWCOUNT;

GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'UnregisterGrainActivationKey',
	'EXECUTE UnregisterGrainActivation @ClusterId = @ClusterId, @ProviderId = @ProviderId, @GrainId = @GrainId, @ActivationId = @ActivationId'
GO


/* 查找现有的 Grain 激活实例。 */
CREATE PROCEDURE LookupGrainActivation
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @GrainId VARCHAR(600)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 如果条目存在，则获取该条目。 */
/* 同时预先锁定哈希索引页，防止与注册操作发生死锁。 */
SELECT
    ClusterId,
    ProviderId,
    GrainId,
    SiloAddress,
    ActivationId
FROM
    OrleansGrainDirectory
WHERE
    ClusterId = @ClusterId
    AND ProviderId = @ProviderId
    AND GrainId = @GrainId;

GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'LookupGrainActivationKey',
	'EXECUTE LookupGrainActivation @ClusterId = @ClusterId, @ProviderId = @ProviderId, @GrainId = @GrainId'
GO


/* 注销指定 Silo 中的所有 Grain 激活实例。 */
CREATE PROCEDURE UnregisterGrainActivations
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @SiloAddresses VARCHAR(MAX)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

/* 如果条目存在，则删除这些条目。 */
/* 对整个表加排他锁，防止与注册操作发生死锁。 */
DELETE OrleansGrainDirectory
FROM OrleansGrainDirectory WITH (TABLOCKX)
WHERE
    ClusterId = @ClusterId
    AND ProviderId = @ProviderId
    AND SiloAddress IN (SELECT Value FROM STRING_SPLIT(@SiloAddresses, '|'));

SELECT @@ROWCOUNT;

COMMIT TRANSACTION;

GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'UnregisterGrainActivationsKey',
	'EXECUTE UnregisterGrainActivations @ClusterId = @ClusterId, @ProviderId = @ProviderId, @SiloAddresses = @SiloAddresses'
GO

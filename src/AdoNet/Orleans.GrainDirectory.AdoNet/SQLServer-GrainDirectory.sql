/*
Orleans Grain 目录。
此表格存储集中所有 grains 的位置。

注:
ClusterId、 ProviderId和 GrainId 的组合构成 OrleansGrainDirectory 表格的主要关键字。
这些列加起来达到 SQL 服务器索引(900字节)允许的最大关键字大小。
应注意不要增加这些栏的长度,因为其可能超过 SQL 服务器关键尺寸的限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 确定群集实例 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 指定 grain 目录提供者 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 以文本格式持有 grain id */
    GrainId VARCHAR(600) NOT NULL,

    /* 握住 silo 地址 grain 位于 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 将激活id 保存在 silo 所在位置 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 持有将 grain 添加到目录的时间 */
    CreatedOn DATETIMEOFFSET(3) NOT NULL,

    /* 指定一个独有的 grain 激活 */
    CONSTRAINT PK_OrleansGrainDirectory PRIMARY KEY CLUSTERED
    (
        ClusterId ASC,
        ProviderId ASC,
        GrainId ASC
    )
)
GO

/* 注册新的 grain 激活 */
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

/* 如果存在的话, 获取现有的条目 。 */
/* 并锁定前方的射程, 以防需要添加新条目 。 */
/* 防止僵局需要这一锁。 */
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

    /* 如果条目已经存在, 我们可以归还它。 */
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

    /* 如果还没有条目,我们现在将加入。 */
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

/* 取消现有 grain 激活的不登记 */
CREATE PROCEDURE UnregisterGrainActivation
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @GrainId VARCHAR(600),
    @ActivationId VARCHAR(100)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 如果存在,删除该项。 */
/* 从而将散列指数页面锁定在前面, */
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


/* 查看已有的 grain 激活 */
CREATE PROCEDURE LookupGrainActivation
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @GrainId VARCHAR(600)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 如果存在的话, 获取现有的条目 。 */
/* 从而将散列指数页面锁定在前面,以防止登记陷入僵局。 */
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


/* 全部不登记者 grain 指定 silos */
CREATE PROCEDURE UnregisterGrainActivations
    @ClusterId VARCHAR(150),
    @ProviderId VARCHAR(150),
    @SiloAddresses VARCHAR(MAX)
AS

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

/* 如果条目存在,则删除它们。 */
/* 这为整个谈判桌设置了专用锁,以防止登记工作陷入僵局。 */
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

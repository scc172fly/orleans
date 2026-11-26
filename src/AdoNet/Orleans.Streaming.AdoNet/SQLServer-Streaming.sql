/*
Orleans 串流消息序列。
此序列减少了 [MessageId] 值的生成与身份列的产生之间的争论。
CACE参数可以增加,以进一步减少争议。
*/
CREATE SEQUENCE OrleansStreamMessageSequence
AS BIGINT
START WITH 1
INCREMENT BY 1
NO MAXVALUE
NO CYCLE
CACHE 1000;
GO

/*
Orleans 流动信件队列

此表格存储队列的等待 Orleans处理的信件。

要求编制本表的要求如下:

1. 表格中增加新行时,只看到尾部的插入。
2. 表格将用高频进行抽查,以保留第一批符合众所周知的标准( " 可看见"和 " 未过期 " 和 " 最大尝试 " )的行。
3. 表格将看到,在确认信息后,正删除各行头。
4. 表格将看到,随着过期信息被移到死字,各行头将删除。
5. 由于上述询问每次涉及不止一行,有可能出现僵局。
6. 少数错误或有毒信息在被移到死字之前,头部可能停留一段时间。
7. 表格偶尔会变成空的或至少是随着集群成功地赶上所有信息而变得稀少。

虽然[1-6] 随着时间的推移所有原因都造成页块分割,但[7] 通过允许 sql 服务器最终删除所有页面,自行解决这一退化。
因此,设计试图优化[2],同时假设由此造成的退化最终会自行解决。

设计还试图尽量减少僵局的可能性,而牺牲了更高的锁定争议。
这样做的方式是,迫使所有查询都按组群指数的完全相同顺序接触数据。
这导致订购资源锁定获取,同时避免了订购本身的费用。

*/
CREATE TABLE OrleansStreamMessage
(
	/* 确定申请 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 确定申请内供应商的身份 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 指定在提供者中配置的单个队列碎片*/
	QueueId NVARCHAR(150) NOT NULL,

	/* 队列信件的独特升序数 */
	MessageId BIGINT NOT NULL,

	/* 事件被取消的次数 */
	Dequeued INT NOT NULL,

	/* 活动将可见的UTC时间 */
	VisibleOn DATETIME2(7) NOT NULL,

	/* 事件即将结束的时点UTC */
	ExpiresOn DATETIME2(7) NOT NULL,

    /* 创建事件时的 UTC 时间 - 只排除故障 */
	CreatedOn DATETIME2(7) NOT NULL,

    /* 更新该事件时的UTC时间 - 只排除故障 */
	ModifiedOn DATETIME2(7) NOT NULL,

	/* 任意大片事件有效载荷 */
	Payload VARBINARY(MAX) NOT NULL,

	/* 这个分组式的PK支持各种有秩序扫描查询。 */
	CONSTRAINT PK_OrleansStreamMessage PRIMARY KEY CLUSTERED
	(
		ServiceId ASC,
        ProviderId ASC,
		QueueId ASC,
		MessageId ASC
	)
);
GO

/*
Orleans 串流死亡信

本表列出无法在允许的尝试次数内处理或过期的事件。
*/
CREATE TABLE OrleansStreamDeadLetter
(
	/* 确定申请 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 确定申请内供应商的身份 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 指定在提供者中配置的单个队列碎片*/
	QueueId NVARCHAR(150) NOT NULL,

	/* 队列信件的独特升序数 */
	MessageId BIGINT NOT NULL,

	/* 事件被取消的次数 */
	Dequeued INT NOT NULL,

	/* 活动将可见的UTC时间 */
	VisibleOn DATETIME2(7) NOT NULL,

	/* 事件即将结束的时点UTC */
	ExpiresOn DATETIME2(7) NOT NULL,

    /* 创建事件时的 UTC 时间 - 只排除故障 */
	CreatedOn DATETIME2(7) NOT NULL,

    /* 更新该事件时的UTC时间 - 只排除故障 */
	ModifiedOn DATETIME2(7) NOT NULL,

    /* 放弃活动时的UTC时间 -- -- 只排除故障 */
	DeadOn DATETIME2(7) NOT NULL,

	/* 将事件从死信中删除的UTC时间 */
	RemoveOn DATETIME2(7) NOT NULL,

	/* 任意大片事件有效载荷 */
	Payload VARBINARY(MAX) NULL,

	/* 这个分组式的PK支持各种有秩序扫描查询。 */
    /* 其主要目的是帮助分割更新行锁,以尽量减少解开争论。 */
	CONSTRAINT PK_OrleansStreamDeadLetter PRIMARY KEY CLUSTERED
	(
		ServiceId ASC,
        ProviderId ASC,
		QueueId ASC,
		MessageId ASC
	)
);
GO

/*
Orleans 流式控制表。
本表包含用于帮助提供者自行管理自身工作的调度变量。
*/
CREATE TABLE OrleansStreamControl
(
	/* 确定申请 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 确定申请内供应商的身份 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 指定在提供者中配置的单个队列碎片 */
	QueueId NVARCHAR(150) NOT NULL,

    /* 下一步要将信件搬迁的到期时间排 */
    EvictOn DATETIME2(7) NOT NULL,

    /* 每个行代表单个队列的平面配置对象 */
	CONSTRAINT PK_OrleansStreamControl PRIMARY KEY CLUSTERED
	(
		ServiceId ASC,
        ProviderId ASC,
		QueueId ASC
	)
);
GO

/* 将消息排到 Orleans 串列中的信件队列 */
CREATE PROCEDURE QueueStreamMessage
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
	@Payload VARBINARY(MAX),
	@ExpiryTimeout INT
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
MessageIds 必须在每一队列内分配次序中显示。 否则,
后 MessageId 将插入较早的
仍然未承诺, 违反溪流缓存的单声道序列 变换。
*/
DECLARE @LockResource NVARCHAR(255) = CONCAT
(
    N'OrleansStreamMessage:',
    CONVERT
    (
        VARCHAR(64),
        HASHBYTES
        (
            'SHA2_256',
            CONCAT
            (
                DATALENGTH(@ServiceId), N':', @ServiceId,
                DATALENGTH(@ProviderId), N':', @ProviderId,
                DATALENGTH(@QueueId), N':', @QueueId
            )
        ),
        2
    )
);
DECLARE @LockResult INT;
DECLARE @StartedTransaction BIT = 0;

BEGIN TRY
    IF @@TRANCOUNT = 0
    BEGIN
        BEGIN TRANSACTION;
        SET @StartedTransaction = 1;
    END;

    EXECUTE @LockResult = sys.sp_getapplock
        @Resource = @LockResource,
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction';

    IF @LockResult < 0
    BEGIN
        THROW 51000, 'Failed to acquire the stream message queue lock.', 1;
    END;

    DECLARE @MessageId BIGINT = NEXT VALUE FOR OrleansStreamMessageSequence;
    DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();
    DECLARE @ExpiresOn DATETIME2(7) = DATEADD(SECOND, @ExpiryTimeout, @Now);

    INSERT INTO OrleansStreamMessage
    (
        ServiceId,
        ProviderId,
        QueueId,
        MessageId,
        Dequeued,
        VisibleOn,
        ExpiresOn,
        CreatedOn,
        ModifiedOn,
        Payload
    )
    OUTPUT
        Inserted.ServiceId,
        Inserted.ProviderId,
        Inserted.QueueId,
        Inserted.MessageId
    VALUES
    (
        @ServiceId,
        @ProviderId,
        @QueueId,
        @MessageId,
        0,
        @Now,
        @ExpiresOn,
        @Now,
        @Now,
        @Payload
    );

    IF @StartedTransaction = 1
    BEGIN
        COMMIT TRANSACTION;
    END;
END TRY
BEGIN CATCH
    IF @StartedTransaction = 1 AND XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'QueueStreamMessageKey',
	'EXECUTE QueueStreamMessage @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @Payload = @Payload, @ExpiryTimeout = @ExpiryTimeout'
GO

/* 从 Orleans 串流信件队列中获取批发的信件 */
/* 还应利用机会在应进行驱逐活动时开展驱逐活动 */
CREATE PROCEDURE GetStreamMessages
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
    @MaxCount INT,
	@MaxAttempts INT,
	@VisibilityTimeout INT,
    @RemovalTimeout INT,
    @EvictionInterval INT,
    @EvictionBatchSize INT
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();
DECLARE @VisibleOn DATETIME2(7) = DATEADD(SECOND, @VisibilityTimeout, @Now);

/* 轻量检查,以确定是否应该进行驱逐活动 */
DECLARE @EvictOn DATETIME2(7) =
(
    SELECT EvictOn
    FROM OrleansStreamControl
    WHERE
        ServiceId = @ServiceId
        AND ProviderId = @ProviderId
        AND QueueId = @QueueId
);

/* 只有当一项活动应引起 */
IF @EvictOn IS NULL OR @EvictOn < @Now
BEGIN

    /* 试图赢得更新时间表的竞赛 */
    /* 必要时,此程序还将初始化表格 */
    WITH Candidate AS
    (
        SELECT
            ServiceId = @ServiceId,
            ProviderId = @ProviderId,
            QueueId = @QueueId,
            Now = @Now,
            EvictOn = DATEADD(SECOND, @EvictionInterval, @Now)
    )
    MERGE OrleansStreamControl WITH (UPDLOCK, HOLDLOCK) AS T
    USING Candidate AS S
    ON T.ServiceId = S.ServiceId
    AND T.ProviderId = S.ProviderId
    AND T.QueueId = S.QueueId
    WHEN MATCHED AND T.EvictOn < S.Now THEN
    UPDATE SET T.EvictOn = S.EvictOn
    WHEN NOT MATCHED BY TARGET THEN
    INSERT
    (
        ServiceId,
        ProviderId,
        QueueId,
        EvictOn
    )
    VALUES
    (
        ServiceId,
        ProviderId,
        QueueId,
        EvictOn
    );

    /* 如果上述声明赢得了比赛,那么我们也可以进行驱逐 */
    /* 其他同时进行的查询将继续正常运行,直至下次适当时间 */
    IF (@@ROWCOUNT > 0)
    BEGIN

        /* 驱逐信件 */
        EXECUTE EvictStreamMessages
            @ServiceId = @ServiceId,
            @ProviderId = @ProviderId,
            @QueueId = @QueueId,
            @MaxAttempts = @MaxAttempts,
            @RemovalTimeout = @RemovalTimeout,
            @BatchSize = @EvictionBatchSize

        /* 驱逐死亡信 */
        EXECUTE EvictStreamDeadLetters
            @ServiceId = @ServiceId,
            @ProviderId = @ProviderId,
            @QueueId = @QueueId,
            @BatchSize = @EvictionBatchSize;
            
    END;

END;

/* 以与分组索引相同的顺序更新信息,以避免与其他查询陷入僵局 */
WITH Batch AS
(
	SELECT TOP (@MaxCount)
		ServiceId,
        ProviderId,
		QueueId,
		MessageId,
		Dequeued,
		VisibleOn,
		ExpiresOn,
		CreatedOn,
		ModifiedOn,
		Payload
	FROM
		OrleansStreamMessage WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
	WHERE
		ServiceId = @ServiceId
        AND ProviderId = @ProviderId
		AND QueueId = @QueueId
		AND Dequeued < @MaxAttempts
		AND VisibleOn <= @Now
		AND ExpiresOn > @Now
	ORDER BY
        ServiceId,
        ProviderId,
        QueueId,
		MessageId
)
UPDATE Batch
SET
	Dequeued += 1,
	VisibleOn = @VisibleOn,
	ModifiedOn = @Now
OUTPUT
	Inserted.ServiceId,
    Inserted.ProviderId,
	Inserted.QueueId,
	Inserted.MessageId,
	Inserted.Dequeued,
	Inserted.VisibleOn,
	Inserted.ExpiresOn,
	Inserted.CreatedOn,
	Inserted.ModifiedOn,
	Inserted.Payload
FROM
	Batch;

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'GetStreamMessagesKey',
	'EXECUTE GetStreamMessages @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @MaxCount = @MaxCount, @MaxAttempts = @MaxAttempts, @VisibilityTimeout = @VisibilityTimeout, @RemovalTimeout = @RemovalTimeout, @EvictionInterval = @EvictionInterval, @EvictionBatchSize = @EvictionBatchSize'
GO

/* 确认发送流信息。 */
CREATE PROCEDURE ConfirmStreamMessages
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
    @Items NVARCHAR(MAX)
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 分析要删除的信件标识符 */
DECLARE @ItemsTable TABLE
(
    MessageId BIGINT PRIMARY KEY NOT NULL,
    Dequeued INT NOT NULL
);
WITH Items AS
(
	SELECT Value FROM STRING_SPLIT(@Items, '|')
)
INSERT INTO @ItemsTable
(
    MessageId,
    Dequeued
)
SELECT
	CAST(SUBSTRING(Value, 1, CHARINDEX(':', Value, 1) - 1) AS BIGINT) AS MessageId,
	CAST(SUBSTRING(Value, CHARINDEX(':', Value, 1) + 1, LEN(Value)) AS INT) AS Dequeued
FROM
	Items;

/* 计算要删除的信件数,以便我们在下一个查询中使用顺序 */
DECLARE @Count INT = (SELECT COUNT(*) FROM @ItemsTable);

/* 立即再交付的收据发布信息 */
IF EXISTS (SELECT 1 FROM @ItemsTable WHERE Dequeued < 0)
BEGIN
    DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();

    WITH Batch AS
    (
        SELECT TOP (@Count)
            M.*
        FROM
            OrleansStreamMessage AS M WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK, ROWLOCK)
        WHERE
            ServiceId = @ServiceId
            AND ProviderId = @ProviderId
            AND QueueId = @QueueId
            AND EXISTS
            (
                SELECT *
                FROM @ItemsTable AS I
                WHERE I.MessageId = M.MessageId
                AND -I.Dequeued = M.Dequeued
            )
        ORDER BY
            ServiceId,
            ProviderId,
            QueueId,
            MessageId
    )
    UPDATE Batch
    SET
        VisibleOn = @Now,
        ModifiedOn = @Now
    OUTPUT
        Inserted.ServiceId,
        Inserted.ProviderId,
        Inserted.QueueId,
        Inserted.MessageId;

    RETURN;
END;

/* 删除的电文顺序与分组索引相同,以避免与其他查询陷入僵局 */
/* 正在同时更改信件, 因为其删除的接收可能不再匹配 */
WITH Batch AS
(
	SELECT TOP (@Count)
		*
	FROM
		OrleansStreamMessage AS M WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK, ROWLOCK)
	WHERE
		ServiceId = @ServiceId
        AND ProviderId = @ProviderId
		AND QueueId = @QueueId
        AND EXISTS
        (
            SELECT *
            FROM @ItemsTable AS I
            WHERE I.MessageId = M.MessageId
            AND I.Dequeued = M.Dequeued
        )
	ORDER BY
        ServiceId,
        ProviderId,
        QueueId,
		MessageId
)
DELETE FROM Batch
OUTPUT
    Deleted.ServiceId,
    Deleted.ProviderId,
    Deleted.QueueId,
    Deleted.MessageId;

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'ConfirmStreamMessagesKey',
	'EXECUTE ConfirmStreamMessages @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @Items = @Items'
GO

/* 对特定电文适用交付失败规则。 */
/* 如果信息被解密太多次,我们把它移到死信桌。 */
/* 如果消息过期了 我们就转到死信桌 */
/* 如果该电文仍然有资格发送,则再次显示。 */
CREATE PROCEDURE FailStreamMessage
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
    @MessageId BIGINT,
	@MaxAttempts INT,
	@RemovalTimeout INT
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();
DECLARE @RemoveOn DATETIME2(7) = DATEADD(SECOND, @RemovalTimeout, @Now);

/* 如果该信息仍然可以解排, 那么试图再次显示它标记 */
UPDATE OrleansStreamMessage
SET
    VisibleOn = @Now,
    ModifiedOn = @Now
WHERE
    ServiceId = @ServiceId
    AND ProviderId = @ProviderId
    AND QueueId = @QueueId
    AND MessageId = @MessageId
    AND Dequeued < @MaxAttempts;

IF @@ROWCOUNT > 0 RETURN;

/* 试图将电文移到死字 */
DELETE FROM OrleansStreamMessage
OUTPUT
    Deleted.ServiceId,
    Deleted.ProviderId,
    Deleted.QueueId,
    Deleted.MessageId,
    Deleted.Dequeued,
    Deleted.VisibleOn,
    Deleted.ExpiresOn,
    Deleted.CreatedOn,
    Deleted.ModifiedOn,
    @Now AS DeadOn,
    @RemoveOn AS RemoveOn,
    Deleted.Payload
INTO OrleansStreamDeadLetter
(
    ServiceId,
    ProviderId,
    QueueId,
    MessageId,
    Dequeued,
    VisibleOn,
    ExpiresOn,
    CreatedOn,
    ModifiedOn,
    DeadOn,
    RemoveOn,
    Payload
)
WHERE
    ServiceId = @ServiceId
    AND ProviderId = @ProviderId
    AND QueueId = @QueueId
    AND MessageId = @MessageId;

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'FailStreamMessageKey',
	'EXECUTE FailStreamMessage @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @MessageId = @MessageId, @MaxAttempts = @MaxAttempts, @RemovalTimeout = @RemovalTimeout'
GO

/* 将未发送的信件从电文表格移到死信表格,用于人类排除故障。 */
CREATE PROCEDURE EvictStreamMessages
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
	@BatchSize INT,
	@MaxAttempts INT,
	@RemovalTimeout INT
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();
DECLARE @RemoveOn DATETIME2(7) = DATEADD(SECOND, @RemovalTimeout, @Now);

/* 删除的电文顺序与分组索引相同,以避免与其他查询陷入僵局 */
WITH Batch AS
(
	SELECT TOP (@BatchSize)
		ServiceId,
        ProviderId,
		QueueId,
		MessageId,
		Dequeued,
		VisibleOn,
		ExpiresOn,
		CreatedOn,
		ModifiedOn,
		DeadOn = @Now,
		RemoveOn = @RemoveOn,
		Payload
	FROM
		OrleansStreamMessage WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
	WHERE
		ServiceId = @ServiceId
        AND ProviderId = @ProviderId
		AND QueueId = @QueueId

        -- 给电文提供完成
        AND VisibleOn <= @Now
		AND
		(
			-- 信息被解密过多次
			Dequeued >= @MaxAttempts
			OR
			-- 信件过期
			ExpiresOn <= @Now
		)
	ORDER BY
        ServiceId,
        ProviderId,
        QueueId,
		MessageId
)
DELETE FROM Batch
OUTPUT
	Deleted.ServiceId,
    Deleted.ProviderId,
	Deleted.QueueId,
	Deleted.MessageId,
	Deleted.Dequeued,
	Deleted.VisibleOn,
	Deleted.ExpiresOn,
	Deleted.CreatedOn,
	Deleted.ModifiedOn,
	Deleted.DeadOn,
	Deleted.RemoveOn,
	Deleted.Payload
INTO OrleansStreamDeadLetter
(
	ServiceId,
    ProviderId,
	QueueId,
	MessageId,
	Dequeued,
	VisibleOn,
	ExpiresOn,
	CreatedOn,
	ModifiedOn,
	DeadOn,
	RemoveOn,
	Payload
);

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'EvictStreamMessagesKey',
	'EXECUTE EvictStreamMessages @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @BatchSize = @BatchSize, @MaxAttempts = @MaxAttempts, @RemovalTimeout = @RemovalTimeout'
GO

/* 从已死字母表格中删除信息。 */
CREATE PROCEDURE EvictStreamDeadLetters
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
	@BatchSize INT
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Now DATETIME2(7) = SYSUTCDATETIME();

/* 删除的电文顺序与分组索引相同,以避免与其他查询陷入僵局 */
WITH Batch AS
(
    SELECT TOP (@BatchSize)
        ServiceId,
        ProviderId,
        QueueId,
        MessageId
    FROM
        OrleansStreamDeadLetter WITH (UPDLOCK)
    WHERE
        ServiceId = @ServiceId
        AND ProviderId = @ProviderId
        AND QueueId = @QueueId
        AND RemoveOn <= @Now
    ORDER BY
        ServiceId,
        ProviderId,
        QueueId,
        MessageId
)
DELETE FROM Batch;

END
GO

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'EvictStreamDeadLettersKey',
	'EXECUTE EvictStreamDeadLetters @ServiceId = @ServiceId, @ProviderId = @ProviderId, @QueueId = @QueueId, @BatchSize = @BatchSize'
GO

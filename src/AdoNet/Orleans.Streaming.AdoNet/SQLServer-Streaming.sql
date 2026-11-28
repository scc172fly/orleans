/*
Orleans 流消息序列。
与标识列相比，此序列可减少生成 [MessageId] 值时的争用。
可增大 CACHE 参数以进一步减少争用。
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
Orleans 流消息队列。

此表存储等待 Orleans 处理的入队消息。

此表需要满足以下要求：

1. 添加新行时，仅在表尾插入。
2. 高频轮询此表，以预留第一批符合既定条件的行（可见、未过期且未达到最大尝试次数）。
3. 确认消息时，从表头移除行。
4. 将过期消息移至死信表时，从表头移除行。
5. 由于上述查询一次涉及多行，可能发生死锁。
6. 少量处理失败的消息或毒消息可能在表头滞留一段时间，之后才移至死信表。
7. 当集群成功处理完所有待处理消息时，表偶尔会变为空，或至少变得稀疏。

虽然 [1-6] 随时间均会导致页碎片，但 [7] 允许 SQL Server 最终移除所有页，从而自行消除这种性能下降。
因此，此设计尝试针对 [2] 进行优化，同时假定由此产生的性能下降最终会自行消除。

该设计还试图以增加锁争用为代价，尽可能降低死锁的概率。
这是通过强制所有查询严格按照聚集索引的顺序访问数据来实现的。
这样既能按顺序获取资源锁，又避免额外排序的开销。

*/
CREATE TABLE OrleansStreamMessage
(
	/* 标识应用程序。 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 标识应用程序中的提供程序。 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 标识提供程序配置的单个队列分片。 */
	QueueId NVARCHAR(150) NOT NULL,

	/* 入队消息的唯一递增编号。 */
	MessageId BIGINT NOT NULL,

	/* 事件出队的次数。 */
	Dequeued INT NOT NULL,

	/* 事件变为可见的 UTC 时间。 */
	VisibleOn DATETIME2(7) NOT NULL,

	/* 事件过期的 UTC 时间。 */
	ExpiresOn DATETIME2(7) NOT NULL,

    /* 事件创建的 UTC 时间，仅用于故障排查。 */
	CreatedOn DATETIME2(7) NOT NULL,

    /* 事件更新的 UTC 时间，仅用于故障排查。 */
	ModifiedOn DATETIME2(7) NOT NULL,

	/* 任意大小的事件有效载荷。 */
	Payload VARBINARY(MAX) NOT NULL,

	/* 此聚集主键支持各种有序扫描查询。 */
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
Orleans 流死信表。

此表存储未能在允许的尝试次数内完成处理的事件，或已经过期的事件。
*/
CREATE TABLE OrleansStreamDeadLetter
(
	/* 标识应用程序。 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 标识应用程序中的提供程序。 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 标识提供程序配置的单个队列分片。 */
	QueueId NVARCHAR(150) NOT NULL,

	/* 入队消息的唯一递增编号。 */
	MessageId BIGINT NOT NULL,

	/* 事件出队的次数。 */
	Dequeued INT NOT NULL,

	/* 事件变为可见的 UTC 时间。 */
	VisibleOn DATETIME2(7) NOT NULL,

	/* 事件过期的 UTC 时间。 */
	ExpiresOn DATETIME2(7) NOT NULL,

    /* 事件创建的 UTC 时间，仅用于故障排查。 */
	CreatedOn DATETIME2(7) NOT NULL,

    /* 事件更新的 UTC 时间，仅用于故障排查。 */
	ModifiedOn DATETIME2(7) NOT NULL,

    /* 放弃处理事件的 UTC 时间，仅用于故障排查。 */
	DeadOn DATETIME2(7) NOT NULL,

	/* 计划将事件从死信表中移除的 UTC 时间。 */
	RemoveOn DATETIME2(7) NOT NULL,

	/* 任意大小的事件有效载荷。 */
	Payload VARBINARY(MAX) NULL,

	/* 此聚集主键支持各种有序扫描查询。 */
    /* 其主要目的是对行级更新锁进行分区，以尽量减少出队时的争用。 */
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
Orleans 流控制表。
此表保存计划变量，帮助提供程序自行管理其工作。
*/
CREATE TABLE OrleansStreamControl
(
	/* 标识应用程序。 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 标识应用程序中的提供程序。 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 标识提供程序配置的单个队列分片。 */
	QueueId NVARCHAR(150) NOT NULL,

    /* 下一次消息清理的计划时间。 */
    EvictOn DATETIME2(7) NOT NULL,

    /* 每行代表单个队列的扁平配置对象。 */
	CONSTRAINT PK_OrleansStreamControl PRIMARY KEY CLUSTERED
	(
		ServiceId ASC,
        ProviderId ASC,
		QueueId ASC
	)
);
GO

/* 将消息加入 Orleans 流消息队列。 */
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
MessageId 必须在各自队列内按分配顺序变为可见。否则，
并发事务可能在较早的插入尚未提交时暴露较晚分配的 MessageId，
从而违反流缓存的单调序列不变量。
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

/* 从 Orleans 流消息队列获取消息批次。 */
/* 还会在清理活动到期时择机执行这些活动。 */
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

/* 轻量级检查是否有到期的清理活动。 */
DECLARE @EvictOn DATETIME2(7) =
(
    SELECT EvictOn
    FROM OrleansStreamControl
    WHERE
        ServiceId = @ServiceId
        AND ProviderId = @ProviderId
        AND QueueId = @QueueId
);

/* 仅当清理任务到期时，才进一步尝试执行清理。 */
IF @EvictOn IS NULL OR @EvictOn < @Now
BEGIN

    /* 尝试在更新计划时间的竞争中获胜。 */
    /* 必要时，此操作也会初始化该表。 */
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

    /* 如果上述语句竞争成功，则同时获得执行清理的资格。 */
    /* 其他并发查询将继续正常运行，直到下一次计划时间。 */
    IF (@@ROWCOUNT > 0)
    BEGIN

        /* 清理消息。 */
        EXECUTE EvictStreamMessages
            @ServiceId = @ServiceId,
            @ProviderId = @ProviderId,
            @QueueId = @QueueId,
            @MaxAttempts = @MaxAttempts,
            @RemovalTimeout = @RemovalTimeout,
            @BatchSize = @EvictionBatchSize

        /* 清理死信。 */
        EXECUTE EvictStreamDeadLetters
            @ServiceId = @ServiceId,
            @ProviderId = @ProviderId,
            @QueueId = @QueueId,
            @BatchSize = @EvictionBatchSize;
            
    END;

END;

/* 严格按照聚集索引顺序更新消息，以避免与其他查询发生死锁。 */
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

/* 确认流消息已投递。 */
CREATE PROCEDURE ConfirmStreamMessages
	@ServiceId NVARCHAR(150),
    @ProviderId NVARCHAR(150),
	@QueueId NVARCHAR(150),
    @Items NVARCHAR(MAX)
AS
BEGIN

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 解析要删除的消息标识符。 */
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

/* 统计待删除消息数量，以便在下一条查询中使用 ORDER BY。 */
DECLARE @Count INT = (SELECT COUNT(*) FROM @ItemsTable);

/* 负值出队回执会释放消息，使其立即重新投递。 */
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

/* 严格按照聚集索引顺序删除消息，以避免与其他查询发生死锁。 */
/* 跳过正在被并发修改的消息，因为其出队回执可能已不再匹配。 */
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

/* 将投递失败规则应用于指定消息。 */
/* 若消息的出队次数过多，则将其移至死信表。 */
/* 若消息已过期，则将其移至死信表。 */
/* 若消息仍符合投递条件，则将其重新设为可见。 */
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

/* 如果消息仍可出队，则尝试将其重新标记为可见。 */
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

/* 否则尝试将消息移至死信表。 */
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

/* 将未投递的消息从消息表移至死信表，供人工排查。 */
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

/* 严格按照聚集索引顺序删除消息，以避免与其他查询发生死锁。 */
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

        -- 该消息已获得完成处理的机会。
        AND VisibleOn <= @Now
		AND
		(
			-- 该消息的出队次数过多。
			Dequeued >= @MaxAttempts
			OR
			-- 该消息已过期。
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

/* 从死信表移除消息。 */
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

/* 严格按照聚集索引顺序删除消息，以避免与其他查询发生死锁。 */
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

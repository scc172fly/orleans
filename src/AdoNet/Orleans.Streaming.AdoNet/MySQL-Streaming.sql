CREATE TABLE OrleansStreamMessageSequence
(
    MessageId BIGINT NOT NULL
);
INSERT INTO OrleansStreamMessageSequence
SELECT 0
WHERE NOT EXISTS (SELECT * FROM OrleansStreamMessageSequence);

DELIMITER $$

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
	VisibleOn DATETIME(6) NOT NULL,

	/* 事件即将结束的时点UTC */
	ExpiresOn DATETIME(6) NOT NULL,

    /* 创建事件时的 UTC 时间 - 只排除故障 */
	CreatedOn DATETIME(6) NOT NULL,

    /* 更新该事件时的UTC时间 - 只排除故障 */
	ModifiedOn DATETIME(6) NOT NULL,

	/* 任意大片事件有效载荷 */
	Payload LONGBLOB NOT NULL,

	/* PK支持各种有秩序的扫描查询。 */
	PRIMARY KEY (ServiceId, ProviderId, QueueId, MessageId)
);

DELIMITER $$

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
	VisibleOn DATETIME(6) NOT NULL,

	/* 事件即将结束的时点UTC */
	ExpiresOn DATETIME(6) NOT NULL,

    /* 创建事件时的 UTC 时间 - 只排除故障 */
	CreatedOn DATETIME(6) NOT NULL,

    /* 更新该事件时的UTC时间 - 只排除故障 */
	ModifiedOn DATETIME(6) NOT NULL,

    /* 放弃活动时的UTC时间 -- -- 只排除故障 */
	DeadOn DATETIME(6) NOT NULL,

	/* 将事件从死信中删除的UTC时间 */
	RemoveOn DATETIME(6) NOT NULL,

	/* 任意大片事件有效载荷 */
	Payload LONGBLOB NULL,

	/* PK支持各种有秩序的扫描查询。 */
	PRIMARY KEY (ServiceId, ProviderId, QueueId, MessageId)
);

DELIMITER $$

CREATE TABLE OrleansStreamControl
(
	/* 确定申请 */
	ServiceId NVARCHAR(150) NOT NULL,

    /* 确定申请内供应商的身份 */
    ProviderId NVARCHAR(150) NOT NULL,

	/* 指定在提供者中配置的单个队列碎片 */
	QueueId NVARCHAR(150) NOT NULL,

    /* 下一步要将信件搬迁的到期时间排 */
    EvictOn DATETIME(6) NOT NULL,

    /* 每个行代表单个队列的平面配置对象 */
	PRIMARY KEY (ServiceId, ProviderId, QueueId)
);

DELIMITER $$

CREATE PROCEDURE QueueStreamMessage
(
    IN _ServiceId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
    IN _QueueId NVARCHAR(150),
    IN _Payload LONGBLOB,
    IN _ExpiryTimeout INT
)
BEGIN

DECLARE _Now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
DECLARE _ExpiresOn DATETIME(6) DEFAULT DATE_ADD(_Now, INTERVAL _ExpiryTimeout SECOND);
DECLARE _MessageId BIGINT;

UPDATE OrleansStreamMessageSequence
SET MessageId = LAST_INSERT_ID(MessageId + 1);

SET _MessageId = LAST_INSERT_ID();

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
VALUES
(
    _ServiceId,
    _ProviderId,
    _QueueId,
    _MessageId,
    0,
    _Now,
    _ExpiresOn,
    _Now,
    _Now,
    _Payload
);

SELECT
    _ServiceId AS ServiceId,
    _ProviderId AS ProviderId,
    _QueueId AS QueueId,
    _MessageId AS MessageId;

END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'QueueStreamMessageKey',
	'CALL QueueStreamMessage(@ServiceId, @ProviderId, @QueueId, @Payload, @ExpiryTimeout)'

DELIMITER $$

CREATE PROCEDURE GetStreamMessages
(
    IN _ServiceId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
	IN _QueueId NVARCHAR(150),
    IN _MaxCount INT,
	IN _MaxAttempts INT,
	IN _VisibilityTimeout INT,
    IN _RemovalTimeout INT,
    IN _EvictionInterval INT,
    IN _EvictionBatchSize INT
)
BEGIN

DECLARE _Now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
DECLARE _VisibleOn DATETIME(6) DEFAULT DATE_ADD(_Now, INTERVAL _VisibilityTimeout SECOND);
DECLARE _NextEvictOn TIMESTAMP(6) DEFAULT DATE_ADD(_Now, INTERVAL _EvictionInterval SECOND);
DECLARE _EvictOn DATETIME(6);
DECLARE _Count INT;

-- 获得下一个驱逐时间表
SET _EvictOn =
(
    SELECT EvictOn
    FROM OrleansStreamControl
    WHERE
        ServiceId = _ServiceId
        AND ProviderId = _ProviderId
        AND QueueId = _QueueId
);

-- 必要时初始化控行
IF _EvictOn IS NULL THEN

    -- 开始初始化控行的种族
    INSERT OrleansStreamControl
    (
        ServiceId,
        ProviderId,
        QueueId,
        EvictOn
    )
    VALUES
    (
        _ServiceId,
        _ProviderId,
        _QueueId,
        _NextEvictOn
    )
    ON DUPLICATE KEY
    UPDATE
        EvictOn = EvictOn;

    -- 读取获胜更新
    SET _EvictOn =
    (
        SELECT EvictOn
        FROM OrleansStreamControl
        WHERE
            ServiceId = _ServiceId
            AND ProviderId = _ProviderId
            AND QueueId = _QueueId
    );

END IF;

IF _EvictOn <= _Now THEN

    -- 用于更新控队列的种族
    UPDATE OrleansStreamControl
    SET EvictOn = _NextEvictOn
    WHERE
        ServiceId = _ServiceId
        AND ProviderId = _ProviderId
        AND QueueId = _QueueId
        AND EvictOn <= _Now;

    -- 如果我们赢了比赛,我们也会被赶到
    IF ROW_COUNT() > 0 THEN
        CALL EvictStreamMessages(_ServiceId, _ProviderId, _QueueId, _MaxAttempts, _RemovalTimeout, _EvictionBatchSize);
        CALL EvictStreamDeadLetters(_ServiceId, _ProviderId, _QueueId, _EvictionBatchSize);
    END IF;

END IF;

START TRANSACTION;

/* 选择一组要解排并锁定的电文 */
CREATE TEMPORARY TABLE _Batch AS
SELECT
	ServiceId,
    ProviderId,
	QueueId,
	MessageId
FROM
    OrleansStreamMessage
WHERE
    ServiceId = _ServiceId
    AND ProviderId = _ProviderId
    AND QueueId = _QueueId
    AND Dequeued < _MaxAttempts
    AND VisibleOn <= _Now
    AND ExpiresOn > _Now
ORDER BY
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
LIMIT _MaxCount
FOR UPDATE SKIP LOCKED;

/* 更新信件批次 */
UPDATE OrleansStreamMessage AS M
INNER JOIN _Batch AS B
    ON M.ServiceId = B.ServiceId
    AND M.ProviderId = B.ProviderId
    AND M.QueueId = B.QueueId
    AND M.MessageId = B.MessageId
SET
    M.Dequeued = M.Dequeued + 1,
    M.VisibleOn = _VisibleOn,
    M.ModifiedOn = _Now;

/* 返回更新批次 */
SELECT
	M.ServiceId,
    M.ProviderId,
	M.QueueId,
	M.MessageId,
	M.Dequeued,
	M.VisibleOn,
	M.ExpiresOn,
	M.CreatedOn,
	M.ModifiedOn,
	M.Payload
FROM
    OrleansStreamMessage AS M
    INNER JOIN _Batch AS B
        ON M.ServiceId = B.ServiceId
        AND M.ProviderId = B.ProviderId
        AND M.QueueId = B.QueueId
        AND M.MessageId = B.MessageId
ORDER BY
    M.MessageId;

DROP TEMPORARY TABLE _Batch;

COMMIT;

END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'GetStreamMessagesKey',
	'CALL GetStreamMessages(@ServiceId, @ProviderId, @QueueId, @MaxCount, @MaxAttempts, @VisibilityTimeout, @RemovalTimeout, @EvictionInterval, @EvictionBatchSize)';

DELIMITER $$

CREATE PROCEDURE ConfirmStreamMessages
(
    IN _ServiceId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
    IN _QueueId NVARCHAR(150),
    IN _Items LONGTEXT
)
BEGIN

DECLARE _Delimiter1 NVARCHAR(1) DEFAULT '|';
DECLARE _Delimiter2 NVARCHAR(1) DEFAULT ':';
DECLARE _Value LONGTEXT;
DECLARE _MessageId BIGINT;
DECLARE _Dequeued INT;

SET _Items = CONCAT(_Items, _Delimiter1);

/* 分析要删除的信件标识符 */
DROP TEMPORARY TABLE IF EXISTS _ItemsTable;
CREATE TEMPORARY TABLE _ItemsTable
(
    ServiceId NVARCHAR(150) NOT NULL,
    ProviderId NVARCHAR(150) NOT NULL,
    QueueId NVARCHAR(150) NOT NULL,
    MessageId BIGINT NOT NULL,
    Dequeued INT NOT NULL,

    PRIMARY KEY (ServiceId, ProviderId, QueueId, MessageId)
);

WHILE LOCATE(_Delimiter1, _Items) > 0 DO

    SET _Value = SUBSTRING_INDEX(_Items, _Delimiter1, 1);
    SET _MessageId = CAST(SUBSTRING_INDEX(_Value, _Delimiter2, 1) AS UNSIGNED);
    SET _Dequeued = CAST(SUBSTRING_INDEX(_Value, _Delimiter2, -1) AS SIGNED);

    INSERT INTO _ItemsTable
    (
        ServiceId,
        ProviderId,
        QueueId,
        MessageId,
        Dequeued
    )
    VALUES
    (
        _ServiceId,
        _ProviderId,
        _QueueId,
        _MessageId,
        _Dequeued
    );

    SET _Items = SUBSTRING(_Items, LOCATE(_Delimiter1, _Items) + 1);

END WHILE;

START TRANSACTION;

/* 选择一组邮件以确认和锁定它们 */
CREATE TEMPORARY TABLE _Batch AS
SELECT
	M.ServiceId,
    M.ProviderId,
    M.QueueId,
    M.MessageId
FROM
	OrleansStreamMessage AS M
    INNER JOIN _ItemsTable AS I
        ON M.ServiceId = I.ServiceId
        AND M.ProviderId = I.ProviderId
        AND M.QueueId = I.QueueId
        AND M.MessageId = I.MessageId
        AND M.Dequeued = ABS(I.Dequeued)
ORDER BY
    M.ServiceId,
    M.ProviderId,
    M.QueueId,
	M.MessageId
FOR UPDATE;

IF EXISTS (SELECT 1 FROM _ItemsTable WHERE Dequeued < 0) THEN
    /* 立即再交付的收据发布信息 */
    UPDATE OrleansStreamMessage AS M
    INNER JOIN _Batch AS B
        ON M.ServiceId = B.ServiceId
        AND M.ProviderId = B.ProviderId
        AND M.QueueId = B.QueueId
        AND M.MessageId = B.MessageId
    SET
        M.VisibleOn = UTC_TIMESTAMP(6),
        M.ModifiedOn = UTC_TIMESTAMP(6);
ELSE
    /* 删除所选批次 */
    DELETE M
    FROM OrleansStreamMessage AS M
    INNER JOIN _Batch AS B
        ON M.ServiceId = B.ServiceId
        AND M.ProviderId = B.ProviderId
        AND M.QueueId = B.QueueId
        AND M.MessageId = B.MessageId;
END IF;

/* 返回当量 */
SELECT
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
FROM
    _Batch;

DROP TEMPORARY TABLE _Batch;
DROP TEMPORARY TABLE _ItemsTable;

COMMIT;
END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'ConfirmStreamMessagesKey',
	'CALL ConfirmStreamMessages(@ServiceId, @ProviderId, @QueueId, @Items)';

DELIMITER $$

CREATE PROCEDURE FailStreamMessage
(
    IN _ServiceId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
    IN _QueueId NVARCHAR(150),
    IN _MessageId BIGINT,
    IN _MaxAttempts INT,
    IN _RemovalTimeout INT
)
BEGIN

DECLARE _Now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
DECLARE _RemoveOn DATETIME(6) DEFAULT DATE_ADD(_Now, INTERVAL _RemovalTimeout SECOND);

/* 如果该信息仍然可以解排, 那么试图再次显示它标记 */
UPDATE OrleansStreamMessage
SET
    VisibleOn = _Now,
    ModifiedOn = _Now
WHERE
    ServiceId = _ServiceId
    AND ProviderId = _ProviderId
    AND QueueId = _QueueId
    AND MessageId = _MessageId
    AND Dequeued < _MaxAttempts;

IF ROW_COUNT() = 0 THEN

    START TRANSACTION;

    /* 试图将电文移到死字 */
    CREATE TEMPORARY TABLE Deleted AS
    SELECT
        *
    FROM
        OrleansStreamMessage
    WHERE
        ServiceId = _ServiceId
        AND ProviderId = _ProviderId
        AND QueueId = _QueueId
        AND MessageId = _MessageId;

    DELETE FROM OrleansStreamMessage
    WHERE
        ServiceId = _ServiceId
        AND ProviderId = _ProviderId
        AND QueueId = _QueueId
        AND MessageId = _MessageId;

    INSERT INTO OrleansStreamDeadLetter
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
    SELECT
        ServiceId,
        ProviderId,
        QueueId,
        MessageId,
        Dequeued,
        VisibleOn,
        ExpiresOn,
        CreatedOn,
        ModifiedOn,
        _Now AS DeadOn,
        _RemoveOn AS RemoveOn,
        Payload
    FROM
        Deleted;

    COMMIT;

END IF;

END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'FailStreamMessageKey',
	'CALL FailStreamMessage(@ServiceId, @ProviderId, @QueueId, @MessageId, @MaxAttempts, @RemovalTimeout)'

DELIMITER $$

CREATE PROCEDURE EvictStreamMessages
(
	IN _ServiceId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
	IN _QueueId NVARCHAR(150),
	IN _BatchSize INT,
	IN _MaxAttempts INT,
	IN _RemovalTimeout INT
)
BEGIN

DECLARE _Now DATETIME(6) DEFAULT UTC_TIMESTAMP();
DECLARE _RemoveOn DATETIME(6) DEFAULT DATE_ADD(_Now, INTERVAL _RemovalTimeout SECOND);

START TRANSACTION;

/* 选择要移动和锁定的批量信件 */
CREATE TEMPORARY TABLE _Batch AS
SELECT
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
FROM
    OrleansStreamMessage
WHERE
    ServiceId = _ServiceId
    AND ProviderId = _ProviderId
    AND QueueId = _QueueId
    AND
	(
		-- 如果最后一次尝试超时, 则电文不再可解析
		(Dequeued >= _MaxAttempts AND VisibleOn <= _Now)
		OR
		-- 已经过期的邮件不再可解排
		(ExpiresOn <= _Now)
	)
ORDER BY
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
LIMIT _BatchSize
FOR UPDATE SKIP LOCKED;

/* 将信息复制到死信 */
INSERT INTO OrleansStreamDeadLetter
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
SELECT
    M.ServiceId,
    M.ProviderId,
    M.QueueId,
    M.MessageId,
    M.Dequeued,
    M.VisibleOn,
    M.ExpiresOn,
    M.CreatedOn,
    M.ModifiedOn,
    _Now,
    _RemoveOn,
    M.Payload
FROM
    OrleansStreamMessage AS M
    INNER JOIN _Batch AS B
        ON M.ServiceId = B.ServiceId
        AND M.ProviderId = B.ProviderId
        AND M.QueueId = B.QueueId
        AND M.MessageId = B.MessageId;

/* 现在删除源代码的已选中消息 */
DELETE M
FROM OrleansStreamMessage AS M
INNER JOIN _Batch AS B
    ON M.ServiceId = B.ServiceId
    AND M.ProviderId = B.ProviderId
    AND M.QueueId = B.QueueId
    AND M.MessageId = B.MessageId;

DROP TEMPORARY TABLE _Batch;

COMMIT;

END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'EvictStreamMessagesKey',
	'CALL EvictStreamMessages(@ServiceId, @ProviderId, @QueueId, @BatchSize, @MaxAttempts, @RemovalTimeout)'
;

DELIMITER $$

CREATE PROCEDURE EvictStreamDeadLetters
(
	_ServiceId NVARCHAR(150),
    _ProviderId NVARCHAR(150),
	_QueueId NVARCHAR(150),
	_BatchSize INT
)
BEGIN

DECLARE _Now DATETIME(6) DEFAULT UTC_TIMESTAMP();

/* 选择要删除的批量信件 */
CREATE TEMPORARY TABLE _Batch AS
SELECT
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
FROM
    OrleansStreamDeadLetter
WHERE
    ServiceId = _ServiceId
    AND ProviderId = _ProviderId
    AND QueueId = _QueueId
    AND RemoveOn <= _Now
ORDER BY
    ServiceId,
    ProviderId,
    QueueId,
    MessageId
LIMIT _BatchSize
FOR UPDATE SKIP LOCKED;

/* 现在删除锁定信件 */
DELETE M
FROM OrleansStreamDeadLetter AS M
INNER JOIN _Batch AS B
    ON M.ServiceId = B.ServiceId
    AND M.ProviderId = B.ProviderId
    AND M.QueueId = B.QueueId
    AND M.MessageId = B.MessageId;

DROP TEMPORARY TABLE _Batch;

END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
	'EvictStreamDeadLettersKey',
	'CALL EvictStreamDeadLetters(@ServiceId, @ProviderId, @QueueId, @BatchSize)'
;

DELIMITER $$
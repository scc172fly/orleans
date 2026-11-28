/*
Orleans Grain 目录。
此表存储集群中所有 Grain 的位置。

注意：
ClusterId、ProviderId 和 GrainId 的组合构成 OrleansGrainDirectory 表的主键。
这些列的总长度达到 MariaDB/MySQL 索引允许的最大键长度（768 字节）。
请勿增加这些列的长度，否则可能超过 MariaDB/MySQL 的键长度限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 标识集群实例。 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 标识 Grain 目录提供程序。 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 存储文本形式的 Grain ID。 */
    GrainId VARCHAR(468) NOT NULL,

    /* 存储 Grain 所在的 Silo 地址。 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 存储该 Grain 在其所在 Silo 中的激活实例 ID。 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 存储 Grain 加入目录的时间。 */
    CreatedOn DATETIME(3) NOT NULL,

    /* 主键确保 Grain 标识的唯一性。 */
    PRIMARY KEY (ClusterId, ProviderId, GrainId)
);

DELIMITER $$

CREATE PROCEDURE RegisterGrainActivation
(
    IN _ClusterId NVARCHAR(150),
    IN _ProviderId NVARCHAR(150),
    IN _GrainId NVARCHAR(468),
    IN _SiloAddress NVARCHAR(100),
    IN _ActivationId NVARCHAR(100)
)
BEGIN

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;

    INSERT INTO OrleansGrainDirectory
    (
        ClusterId,
        ProviderId,
        GrainId,
        SiloAddress,
        ActivationId,
        CreatedOn
    )
    VALUES
    (
        _ClusterId,
        _ProviderId,
        _GrainId,
        _SiloAddress,
        _ActivationId,
        UTC_TIMESTAMP(3)
    )
    ON DUPLICATE KEY UPDATE
        ClusterId = ClusterId;

    -- 返回当前注册记录。
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
        AND GrainId = _GrainId
    FOR UPDATE;

    COMMIT;

END;


DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
    'RegisterGrainActivationKey',
    'CALL RegisterGrainActivation(@ClusterId, @ProviderId, @GrainId, @SiloAddress, @ActivationId);';

DELIMITER $$

CREATE PROCEDURE UnregisterGrainActivation
(
    IN _ClusterId VARCHAR(150),
    IN _ProviderId VARCHAR(150),
    IN _GrainId VARCHAR(468),
    IN _ActivationId VARCHAR(100)
)
BEGIN
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;

    DELETE FROM OrleansGrainDirectory
    WHERE
        ClusterId = _ClusterId
        AND ProviderId = _ProviderId
        AND GrainId = _GrainId
        AND ActivationId = _ActivationId;

    SELECT ROW_COUNT() AS DeletedRows;

    COMMIT;
END;

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
    'UnregisterGrainActivationKey',
	'CALL UnregisterGrainActivation(@ClusterId, @ProviderId, @GrainId, @ActivationId);';

DELIMITER $$

CREATE PROCEDURE LookupGrainActivation
(
    IN _ClusterId VARCHAR(150),
    IN _ProviderId VARCHAR(150),
    IN _GrainId VARCHAR(468)
)
BEGIN
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
END

DELIMITER $$

INSERT INTO OrleansQuery
(
    QueryKey,
    QueryText
)
SELECT
    'LookupGrainActivationKey',
    'CALL LookupGrainActivation(@ClusterId, @ProviderId, @GrainId);';

DELIMITER $$

CREATE PROCEDURE UnregisterGrainActivations
(
    IN _ClusterId VARCHAR(150),
    IN _ProviderId VARCHAR(150),
    IN _SiloAddresses TEXT
)
BEGIN
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    START TRANSACTION;

    -- 将 Silo 地址解析到临时表中，以便批量操作。
    CREATE TEMPORARY TABLE TempSiloAddresses
    (
        SiloAddress VARCHAR(100) NOT NULL,
        Level INT NOT NULL
    );

    INSERT INTO TempSiloAddresses (SiloAddress, Level)
    WITH RECURSIVE SiloAddressesCTE AS
    (
        SELECT
            SUBSTRING_INDEX(_SiloAddresses, '|', 1) AS Value,
            SUBSTRING(_SiloAddresses, CHAR_LENGTH(SUBSTRING_INDEX(_SiloAddresses, '|', 1)) + 2, CHAR_LENGTH(_SiloAddresses)) AS Others,
            1 AS Level
        UNION ALL
        SELECT
            SUBSTRING_INDEX(Others, '|', 1) AS Value,
            SUBSTRING(Others, CHAR_LENGTH(SUBSTRING_INDEX(Others, '|', 1)) + 2, CHAR_LENGTH(Others)) AS Others,
            Level + 1
        FROM SiloAddressesCTE
        WHERE Others != ''
    )
    SELECT Value, Level FROM SiloAddressesCTE;

    DELETE FROM OrleansGrainDirectory
    WHERE
        ClusterId = _ClusterId
        AND ProviderId = _ProviderId
        AND SiloAddress IN (SELECT SiloAddress FROM TempSiloAddresses);

    SELECT ROW_COUNT() AS DeletedRows;

    DROP TEMPORARY TABLE TempSiloAddresses;

    COMMIT;
END

DELIMITER $$

INSERT INTO OrleansQuery
(
	QueryKey,
	QueryText
)
SELECT
    'UnregisterGrainActivationsKey',
    'CALL UnregisterGrainActivations(@ClusterId, @ProviderId, @SiloAddresses);';

DELIMITER ;

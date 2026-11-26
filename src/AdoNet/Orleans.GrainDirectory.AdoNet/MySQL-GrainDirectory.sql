/*
Orleans Grain 目录。
此表格存储集中所有 grains 的位置。

注:
ClusterId、 ProviderId和 GrainId 的组合构成 OrleansGrainDirectory 表格的主要关键字。
这些列加起来达到 MariaDB/MySQL 指数(768字节)允许的最大关键字大小。
应注意不要增加这些栏的长度,因为其可能超过 MariaDB/MySQL的关键尺寸限制。

*/
CREATE TABLE OrleansGrainDirectory
(
    /* 确定群集实例 */
    ClusterId VARCHAR(150) NOT NULL,

    /* 指定 grain 目录提供者 */
    ProviderId VARCHAR(150) NOT NULL,

    /* 以文本格式持有 grain id */
    GrainId VARCHAR(468) NOT NULL,

    /* 握住 silo 地址 grain 位于 */
    SiloAddress VARCHAR(100) NOT NULL,

    /* 将激活id 保存在 silo 所在位置 */
    ActivationId VARCHAR(100) NOT NULL,

    /* 持有将 grain 添加到目录的时间 */
    CreatedOn DATETIME(3) NOT NULL,

    /* 主键确保 grain 身份的独特性 */
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

    -- 恢复目前的登记
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

    -- 将 silo 地址分析为批量操作临时表格
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

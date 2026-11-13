-- 001: SPP CT007 measurement storage on ACQMN01_DEV: table dbo.SPPCT007_BEK, plus what the backend needs to insert.
--
-- The backend never runs this script and never creates or alters database objects. It inserts only by calling
-- dbo.SPPCT007_BEK_Upload, and reads the table with SELECT for the Data Viewer table. Run this by hand in SSMS.
--
-- Safe to run again: existing objects are kept (the procedure is recreated). Batches are separated by GO.
-- The table already exists on ACQMN01_DEV; its definition below is a reference and is skipped there.
--
-- Security model: the backend's login is a member of role SPPCT007_APP only. It may EXECUTE the insert procedure (and
-- use its record type) and SELECT the table. It has no INSERT, UPDATE or DELETE on the table: rows are written only by
-- the procedure (ownership chaining), and nothing updates or deletes rows: the table is append-only.
--
-- The object names below are also in Backend/Repositories/Measurement/SppCt007Database.cs. Rename both together.

-- One row per measurement (capture). The upload's information (drillhole, operator, ...) is repeated on each row.
IF OBJECT_ID(N'dbo.SPPCT007_BEK', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SPPCT007_BEK (
        ID                   BIGINT IDENTITY(1, 1) NOT NULL,  -- clustered key: new rows go to the end (fast inserts)
        UPLOAD_ID            BIGINT            NOT NULL,      -- one value per upload (dbo.SPPCT007_BEK_UPLOAD_SEQ)
        DATASET_HASH         CHAR(64)          NOT NULL,      -- SHA-256 by the backend: the same data is stored once
        SOURCE_SESSION_ID    UNIQUEIDENTIFIER  NOT NULL,      -- the desktop app's measurement session
        CAPTURE_NO           INT               NOT NULL,
        ROW_NO               INT               NOT NULL,      -- position in the uploaded table (sorted by depth)
        DRILLHOLE            NVARCHAR(100)     NOT NULL,
        PROJECT              NVARCHAR(100)     NULL,
        CAMPAIGN             NVARCHAR(100)     NULL,
        OPERATOR             NVARCHAR(256)     NOT NULL,
        NOTES                NVARCHAR(4000)    NULL,
        CREATED_USERNAME     NVARCHAR(256)     NULL,          -- declared by the app (from the CSV)
        UPLOADING_USERNAME   NVARCHAR(256)     NOT NULL,      -- declared by the app (Windows account of the uploader)
        UPLOADED_BY_VERIFIED NVARCHAR(256)     NULL,          -- authenticated by the server, when its hosting provides it
        UPLOADED_AT          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SPPCT007_BEK_UPLOADED_AT DEFAULT SYSDATETIMEOFFSET(),
        START_TIME           DATETIMEOFFSET(7) NOT NULL,
        END_TIME             DATETIMEOFFSET(7) NOT NULL,
        SAMPLE_COUNT         INT               NOT NULL,
        DEPTH_FROM_M         DECIMAL(10, 3)    NOT NULL,
        DEPTH_TO_M           DECIMAL(10, 3)    NOT NULL,
        STEP_CM              AS CAST((DEPTH_TO_M - DEPTH_FROM_M) * 100 AS DECIMAL(10, 1)) PERSISTED,
        TS_MS                BIGINT            NULL,          -- device timestamp, Unix milliseconds
        COUNT0               FLOAT             NULL,          -- CPS: counts per second
        COUNT0_RAW           FLOAT             NULL,
        HDM                  FLOAT             NULL,
        HDM_RAW              FLOAT             NULL,
        DOSE_RATE            FLOAT             NULL,
        CONSTRAINT PK_SPPCT007_BEK PRIMARY KEY CLUSTERED (ID),
        -- A capture of a desktop session is stored once, whichever file it arrives in.
        CONSTRAINT UX_SPPCT007_BEK_SESSION_CAPTURE UNIQUE (SOURCE_SESSION_ID, CAPTURE_NO),
        CONSTRAINT CK_SPPCT007_BEK_DEPTH CHECK (DEPTH_FROM_M >= 0 AND DEPTH_TO_M > DEPTH_FROM_M),
        CONSTRAINT CK_SPPCT007_BEK_TIME CHECK (END_TIME >= START_TIME),
        CONSTRAINT CK_SPPCT007_BEK_COUNTS CHECK (CAPTURE_NO > 0 AND ROW_NO > 0 AND SAMPLE_COUNT >= 0 AND (TS_MS IS NULL OR TS_MS >= 0))
    );
END;
GO

-- Finds a repeated upload (same dataset) without scanning the table.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SPPCT007_BEK_HASH' AND object_id = OBJECT_ID(N'dbo.SPPCT007_BEK'))
    CREATE INDEX IX_SPPCT007_BEK_HASH ON dbo.SPPCT007_BEK (DATASET_HASH) INCLUDE (UPLOAD_ID, UPLOADED_AT);
GO

-- Data Viewer: a drillhole's rows by depth, and the drillhole list.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SPPCT007_BEK_DRILLHOLE' AND object_id = OBJECT_ID(N'dbo.SPPCT007_BEK'))
    CREATE INDEX IX_SPPCT007_BEK_DRILLHOLE ON dbo.SPPCT007_BEK (DRILLHOLE, DEPTH_FROM_M, DEPTH_TO_M)
        INCLUDE (UPLOAD_ID, UPLOADED_AT, PROJECT, CAMPAIGN);
GO

IF OBJECT_ID(N'dbo.SPPCT007_BEK_UPLOAD_SEQ', N'SO') IS NULL
    CREATE SEQUENCE dbo.SPPCT007_BEK_UPLOAD_SEQ AS BIGINT START WITH 1 INCREMENT BY 1 NO CYCLE;
GO

-- The rows of one upload, sent by the backend in a single call (table-valued parameter).
IF TYPE_ID(N'dbo.SPPCT007_BEK_RECORD') IS NULL
    CREATE TYPE dbo.SPPCT007_BEK_RECORD AS TABLE (
        CAPTURE_NO   INT               NOT NULL PRIMARY KEY,  -- a capture appears once per upload
        ROW_NO       INT               NOT NULL,
        START_TIME   DATETIMEOFFSET(7) NOT NULL,
        END_TIME     DATETIMEOFFSET(7) NOT NULL,
        SAMPLE_COUNT INT               NOT NULL,
        DEPTH_FROM_M DECIMAL(10, 3)    NOT NULL,
        DEPTH_TO_M   DECIMAL(10, 3)    NOT NULL,
        TS_MS        BIGINT            NULL,
        COUNT0       FLOAT             NULL,
        COUNT0_RAW   FLOAT             NULL,
        HDM          FLOAT             NULL,
        HDM_RAW      FLOAT             NULL,
        DOSE_RATE    FLOAT             NULL
    );
GO

-- Stores one upload in one transaction. Returns one row: STATUS, UPLOAD_ID, RECORD_COUNT, UPLOADED_AT, where STATUS is
--   Stored          : inserted now;
--   AlreadyUploaded : this exact dataset (same @Hash) was stored before; nothing inserted, the earlier upload returned;
--   Conflict        : some captures of @SessionId were stored by an earlier, different upload; nothing inserted, and a
--                     second result set lists those CAPTURE_NO values.
CREATE OR ALTER PROCEDURE dbo.SPPCT007_BEK_Upload
    @Hash              CHAR(64),
    @SessionId         UNIQUEIDENTIFIER,
    @Drillhole         NVARCHAR(100),
    @Project           NVARCHAR(100)  = NULL,
    @Campaign          NVARCHAR(100)  = NULL,
    @Operator          NVARCHAR(256),
    @Notes             NVARCHAR(4000) = NULL,
    @CreatedUsername   NVARCHAR(256)  = NULL,
    @UploadingUsername NVARCHAR(256),
    @VerifiedUploader  NVARCHAR(256)  = NULL,
    @Records           dbo.SPPCT007_BEK_RECORD READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;  -- any error rolls the whole upload back

    IF @Hash IS NULL OR LEN(@Hash) <> 64 OR @SessionId IS NULL
       OR NULLIF(LTRIM(RTRIM(@Drillhole)), N'') IS NULL OR NULLIF(LTRIM(RTRIM(@Operator)), N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(@UploadingUsername)), N'') IS NULL
        THROW 50001, N'SPPCT007_BEK_Upload: hash, session, drillhole, operator and uploading username are required.', 1;

    IF NOT EXISTS (SELECT 1 FROM @Records)
        THROW 50002, N'SPPCT007_BEK_Upload: at least one record is required.', 1;

    DECLARE @UploadId BIGINT, @UploadedAt DATETIMEOFFSET(7), @Count INT;

    BEGIN TRANSACTION;

    -- UPDLOCK + HOLDLOCK: a concurrent upload of the same dataset (a retry while the first attempt still runs) waits
    -- here until the first one commits, then finds it instead of inserting it twice.
    SELECT TOP (1) @UploadId = UPLOAD_ID, @UploadedAt = UPLOADED_AT
    FROM dbo.SPPCT007_BEK WITH (UPDLOCK, HOLDLOCK)
    WHERE DATASET_HASH = @Hash;

    IF @UploadId IS NOT NULL
    BEGIN
        SELECT @Count = COUNT(*) FROM dbo.SPPCT007_BEK WHERE DATASET_HASH = @Hash;
        COMMIT TRANSACTION;
        SELECT N'AlreadyUploaded' AS STATUS, @UploadId AS UPLOAD_ID, @Count AS RECORD_COUNT, @UploadedAt AS UPLOADED_AT;
        RETURN;
    END;

    IF EXISTS (SELECT 1
               FROM dbo.SPPCT007_BEK t WITH (UPDLOCK, HOLDLOCK)
               JOIN @Records r ON r.CAPTURE_NO = t.CAPTURE_NO
               WHERE t.SOURCE_SESSION_ID = @SessionId)
    BEGIN
        SELECT N'Conflict' AS STATUS, CAST(NULL AS BIGINT) AS UPLOAD_ID, 0 AS RECORD_COUNT, CAST(NULL AS DATETIMEOFFSET(7)) AS UPLOADED_AT;
        SELECT t.CAPTURE_NO
        FROM dbo.SPPCT007_BEK t
        JOIN @Records r ON r.CAPTURE_NO = t.CAPTURE_NO
        WHERE t.SOURCE_SESSION_ID = @SessionId
        ORDER BY t.CAPTURE_NO;
        COMMIT TRANSACTION;
        RETURN;
    END;

    SET @UploadId = NEXT VALUE FOR dbo.SPPCT007_BEK_UPLOAD_SEQ;
    SET @UploadedAt = SYSDATETIMEOFFSET();

    -- One set-based insert for every row of the upload.
    INSERT INTO dbo.SPPCT007_BEK
        (UPLOAD_ID, DATASET_HASH, SOURCE_SESSION_ID, CAPTURE_NO, ROW_NO, DRILLHOLE, PROJECT, CAMPAIGN, OPERATOR, NOTES,
         CREATED_USERNAME, UPLOADING_USERNAME, UPLOADED_BY_VERIFIED, UPLOADED_AT,
         START_TIME, END_TIME, SAMPLE_COUNT, DEPTH_FROM_M, DEPTH_TO_M, TS_MS, COUNT0, COUNT0_RAW, HDM, HDM_RAW, DOSE_RATE)
    SELECT @UploadId, @Hash, @SessionId, r.CAPTURE_NO, r.ROW_NO, @Drillhole, @Project, @Campaign, @Operator, @Notes,
           @CreatedUsername, @UploadingUsername, @VerifiedUploader, @UploadedAt,
           r.START_TIME, r.END_TIME, r.SAMPLE_COUNT, r.DEPTH_FROM_M, r.DEPTH_TO_M, r.TS_MS, r.COUNT0, r.COUNT0_RAW, r.HDM, r.HDM_RAW, r.DOSE_RATE
    FROM @Records r;

    SET @Count = @@ROWCOUNT;
    COMMIT TRANSACTION;

    SELECT N'Stored' AS STATUS, @UploadId AS UPLOAD_ID, @Count AS RECORD_COUNT, @UploadedAt AS UPLOADED_AT;
END;
GO

-- The backend's permissions: insert through the procedure, read the table.
IF DATABASE_PRINCIPAL_ID(N'SPPCT007_APP') IS NULL
    CREATE ROLE SPPCT007_APP;
GO

GRANT EXECUTE ON OBJECT::dbo.SPPCT007_BEK_Upload TO SPPCT007_APP;
GRANT EXECUTE ON TYPE::dbo.SPPCT007_BEK_RECORD TO SPPCT007_APP;
GRANT SELECT ON OBJECT::dbo.SPPCT007_BEK TO SPPCT007_APP;
GO

-- Then, once, by the DBA (not run by this script): give the backend its own login and put it in the role. Do not give
-- it db_datareader, db_datawriter or db_owner; keep the password out of source control (user-secrets / environment).
--
--   CREATE LOGIN spp_ct007_app WITH PASSWORD = N'<strong password>', CHECK_POLICY = ON;   -- on master
--   CREATE USER spp_ct007_app FOR LOGIN spp_ct007_app;                                     -- in this database
--   ALTER ROLE SPPCT007_APP ADD MEMBER spp_ct007_app;
--
-- Check: as spp_ct007_app, INSERT INTO dbo.SPPCT007_BEK ... must fail with "permission denied".

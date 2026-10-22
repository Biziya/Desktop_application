-- 001: measurement uploads from the desktop app's Upload Data page (POST /api/measurements/upload).
-- Run once on the central database (ConnectionStrings:DefaultConnection) by a user allowed to create tables. The
-- backend's own login then needs only SELECT and INSERT on both tables. Safe to run again: existing tables are kept.

IF OBJECT_ID(N'dbo.CT007_MEASUREMENT_UPLOAD', N'U') IS NULL
BEGIN
    -- One row per uploaded measurement session (one imported CSV).
    CREATE TABLE dbo.CT007_MEASUREMENT_UPLOAD (
        UPLOAD_ID            BIGINT IDENTITY(1, 1) NOT NULL,
        DATASET_HASH         CHAR(64)          NOT NULL,  -- SHA-256 of the dataset's content: the same data is stored once
        SOURCE_SESSION_ID    UNIQUEIDENTIFIER  NOT NULL,  -- session_id of the CSV (the desktop app's measurement session)
        DRILLHOLE            NVARCHAR(100)     NOT NULL,
        PROJECT              NVARCHAR(100)     NULL,
        CAMPAIGN             NVARCHAR(100)     NULL,
        OPERATOR             NVARCHAR(256)     NOT NULL,
        NOTES                NVARCHAR(4000)    NULL,
        CREATED_USERNAME     NVARCHAR(256)     NULL,      -- declared by the app (from the CSV); NULL for older exports
        UPLOADING_USERNAME   NVARCHAR(256)     NOT NULL,  -- declared by the app (Windows account of the uploading user)
        UPLOADED_BY_VERIFIED NVARCHAR(256)     NULL,      -- authenticated by the server, when its hosting provides it
        UPLOADED_AT          DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_CT007_MEASUREMENT_UPLOAD_UPLOADED_AT DEFAULT SYSDATETIMEOFFSET(),
        RECORD_COUNT         INT               NOT NULL,
        CONSTRAINT PK_CT007_MEASUREMENT_UPLOAD PRIMARY KEY (UPLOAD_ID),
        CONSTRAINT UX_CT007_MEASUREMENT_UPLOAD_DATASET_HASH UNIQUE (DATASET_HASH)
    );
END;

IF OBJECT_ID(N'dbo.CT007_MEASUREMENT_UPLOAD_RECORD', N'U') IS NULL
BEGIN
    -- The measurement rows of an upload, with their original timestamps and depths.
    CREATE TABLE dbo.CT007_MEASUREMENT_UPLOAD_RECORD (
        UPLOAD_ID         BIGINT            NOT NULL,
        SOURCE_SESSION_ID UNIQUEIDENTIFIER  NOT NULL,
        CAPTURE_NO        INT               NOT NULL,
        ROW_NO            INT               NOT NULL,     -- position in the uploaded table (sorted by depth)
        START_TIME        DATETIMEOFFSET(7) NOT NULL,
        END_TIME          DATETIMEOFFSET(7) NOT NULL,
        SAMPLE_COUNT      INT               NOT NULL,
        DEPTH_FROM_M      DECIMAL(10, 3)    NOT NULL,
        DEPTH_TO_M        DECIMAL(10, 3)    NOT NULL,
        STEP_CM           AS CAST((DEPTH_TO_M - DEPTH_FROM_M) * 100 AS DECIMAL(10, 1)) PERSISTED,
        TS_MS             BIGINT            NULL,         -- device timestamp, Unix milliseconds
        COUNT0            FLOAT             NULL,         -- CPS: counts per second
        COUNT0_RAW        FLOAT             NULL,
        HDM               FLOAT             NULL,
        HDM_RAW           FLOAT             NULL,
        DOSE_RATE         FLOAT             NULL,
        CONSTRAINT PK_CT007_MEASUREMENT_UPLOAD_RECORD PRIMARY KEY (UPLOAD_ID, CAPTURE_NO),
        CONSTRAINT FK_CT007_MEASUREMENT_UPLOAD_RECORD_UPLOAD FOREIGN KEY (UPLOAD_ID) REFERENCES dbo.CT007_MEASUREMENT_UPLOAD (UPLOAD_ID),
        -- A capture of a desktop session is stored once, whichever file it arrives in.
        CONSTRAINT UX_CT007_MEASUREMENT_UPLOAD_RECORD_SOURCE UNIQUE (SOURCE_SESSION_ID, CAPTURE_NO),
        CONSTRAINT CK_CT007_MEASUREMENT_UPLOAD_RECORD_DEPTH CHECK (DEPTH_FROM_M >= 0 AND DEPTH_TO_M > DEPTH_FROM_M),
        CONSTRAINT CK_CT007_MEASUREMENT_UPLOAD_RECORD_TIME CHECK (END_TIME >= START_TIME)
    );
END;

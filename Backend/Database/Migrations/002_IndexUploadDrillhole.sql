-- Optional, after 001: speeds up the Data Viewer page, which lists uploads by drillhole
-- (GET api/measurements/uploaded/drillholes and .../records?drillhole=...). Safe to run again.
IF OBJECT_ID(N'dbo.CT007_MEASUREMENT_UPLOAD', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CT007_MEASUREMENT_UPLOAD_DRILLHOLE'
                   AND object_id = OBJECT_ID(N'dbo.CT007_MEASUREMENT_UPLOAD'))
BEGIN
    CREATE INDEX IX_CT007_MEASUREMENT_UPLOAD_DRILLHOLE
        ON dbo.CT007_MEASUREMENT_UPLOAD (DRILLHOLE, UPLOADED_AT)
        INCLUDE (PROJECT, CAMPAIGN, RECORD_COUNT);
END;

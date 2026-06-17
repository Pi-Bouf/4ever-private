-- Idempotent restore of the two databases from the mounted .bak baselines. Runs once on first boot
-- and skips if the databases already exist (the mssql data volume persists them). Target DB names are
-- what the login server expects (TGlobal_gsp / TGame_gsp).
--
-- NOTE: the MOVE logical names below are the standard RageZone baseline names. If a RESTORE fails with
-- "logical file ... is not part of the backup", run
--   RESTORE FILELISTONLY FROM DISK = '/backups/TGLOBAL_RAGEZONE.bak';
-- and substitute the LogicalName values it reports.

IF DB_ID('TGlobal_gsp') IS NULL
BEGIN
    PRINT 'Restoring TGlobal_gsp...';
    RESTORE DATABASE [TGlobal_gsp] FROM DISK = '/backups/TGLOBAL_RAGEZONE.bak'
    WITH MOVE 'TGLOBAL_Data' TO '/var/opt/mssql/data/TGlobal_gsp.mdf',
         MOVE 'TGLOBAL_Log'  TO '/var/opt/mssql/data/TGlobal_gsp.ldf',
         REPLACE;
END
ELSE
    PRINT 'TGlobal_gsp already exists - skipping.';
GO

IF DB_ID('TGame_gsp') IS NULL
BEGIN
    PRINT 'Restoring TGame_gsp...';
    RESTORE DATABASE [TGame_gsp] FROM DISK = '/backups/TGAME_RAGEZONE.bak'
    WITH MOVE 'TGAME_Data' TO '/var/opt/mssql/data/TGame_gsp.mdf',
         MOVE 'TGAME_Log'  TO '/var/opt/mssql/data/TGame_gsp.ldf',
         REPLACE;
END
ELSE
    PRINT 'TGame_gsp already exists - skipping.';
GO

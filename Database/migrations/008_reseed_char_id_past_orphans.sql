-- Move TCHARTABLE's identity past every character id still referenced by orphaned child rows.
--
-- The RageZone baseline deleted characters from TCHARTABLE but left their rows in child tables
-- (TTITLETABLE ~88k rows, TMEDALS, TLASTCOMPANIONTABLE, TCOMPANIONITEMTABLE, TRANKING), with ids up to
-- ~59097, while the TCHARTABLE identity sits at ~1033. TCreateChar then hands out an id that already has a
-- TTITLETABLE (dwCharID, 0) row and fails with a PK violation (2627) -> login server replies Internal.
--
-- Non-destructive fix: reseed the identity to the highest dwCharID/dwOwnerID found in any TGame table, so
-- new characters never inherit a dead character's rows. The orphans are left alone.
-- Idempotent: only reseeds when the current identity is below that high-water mark.
USE [TGame_gsp];
GO

SET NOCOUNT ON;

DECLARE @maxId INT = ISNULL((SELECT MAX(dwCharID) FROM dbo.TCHARTABLE), 0);
DECLARE @one INT, @sql NVARCHAR(MAX), @t SYSNAME, @c SYSNAME;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name, c.name
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.schema_id = SCHEMA_ID('dbo')
      AND c.name IN ('dwCharID', 'dwOwnerID')
      AND TYPE_NAME(c.user_type_id) = 'int'
      AND t.name <> 'TCHARTABLE'
      AND t.name <> 'TITEMTABLE';   -- TITEMTABLE.dwOwnerID also holds non-character owners (cabinets, posts)

OPEN cur;
FETCH NEXT FROM cur INTO @t, @c;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'SELECT @m = MAX(' + QUOTENAME(@c) + N') FROM dbo.' + QUOTENAME(@t);
    SET @one = NULL;
    EXEC sp_executesql @sql, N'@m INT OUTPUT', @m = @one OUTPUT;
    IF @one > @maxId SET @maxId = @one;
    FETCH NEXT FROM cur INTO @t, @c;
END
CLOSE cur;
DEALLOCATE cur;

IF IDENT_CURRENT('dbo.TCHARTABLE') < @maxId
BEGIN
    DBCC CHECKIDENT ('dbo.TCHARTABLE', RESEED, @maxId) WITH NO_INFOMSGS;
    PRINT 'TCHARTABLE identity reseeded to ' + CAST(@maxId AS VARCHAR(12));
END
GO

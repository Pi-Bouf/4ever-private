-- Remove the "Red Fox" mount (mount 100) that an earlier version of 010_classic_mounts.sql added.
--
-- In the 4Classic client that mount is an unfinished placeholder: its monster (32200) has no 3D object,
-- and summoning it crashed TClient (CTachyonObject::SetPIVOT on a NULL object). The port
-- (Tools/ClassicPort) now skips it and the regenerated 010 no longer contains it, so on a fresh
-- database this migration is a no-op.
--
-- Removes: owned licences (TPETTABLE), inventory copies of its item, the item, the mount row, and its
-- monster when no other mount uses it.
-- NOTE: chart tables load once at startup - RESTART TMapSvr/TWorldSvr after applying.
-- Idempotent.
USE [TGame_gsp];
GO

SET NOCOUNT ON;

DELETE FROM dbo.TPETTABLE WHERE wPetID = 100;
DELETE FROM dbo.TITEMTABLE WHERE wItemID = 11675;
DELETE FROM dbo.TITEMCHART WHERE wItemID = 11675 AND bType = 12 AND wUseValue = 100;
DELETE FROM dbo.TMOUNTCHART WHERE wMountID = 100;
DELETE FROM dbo.TMONSTERCHART
WHERE wID = 32200
  AND NOT EXISTS (SELECT 1 FROM dbo.TMOUNTCHART WHERE wDefMonID = 32200 OR wUpgMonID = 32200);
GO

-- Remove 5 mounts that an earlier version of 010_classic_mounts.sql added but that are unfinished in the
-- 4Classic client itself: their objects have no rider pivot (ID_PIVOT_MOUNT), no textures and (mostly) no
-- animations, so they showed up white, frozen and could not be ridden.
--   138 Turtle (32293), 140 Saber (32295), 141 Rat (32296), 143 Fox (32298), 145 Dragon Element (32300)
-- The port (Tools/ClassicPort) now skips mounts without a rider pivot and the regenerated 010 no longer
-- contains them, so on a fresh database this migration is a no-op.
--
-- Removes: owned licences (TPETTABLE), inventory copies of their items, the items, the mount rows, and
-- their monsters when no other mount uses them.
-- NOTE: chart tables load once at startup - RESTART TMapSvr/TWorldSvr after applying.
-- Idempotent.
USE [TGame_gsp];
GO

SET NOCOUNT ON;

DELETE FROM dbo.TPETTABLE WHERE wPetID IN (138, 140, 141, 143, 145);
DELETE FROM dbo.TITEMTABLE WHERE wItemID IN (25751, 25753, 25754, 25756, 25758);
DELETE FROM dbo.TITEMCHART
WHERE wItemID IN (25751, 25753, 25754, 25756, 25758) AND bType = 12 AND wUseValue IN (138, 140, 141, 143, 145);
DELETE FROM dbo.TMOUNTCHART WHERE wMountID IN (138, 140, 141, 143, 145);
DELETE FROM dbo.TMONSTERCHART
WHERE wID IN (32293, 32295, 32296, 32298, 32300)
  AND NOT EXISTS (SELECT 1 FROM dbo.TMOUNTCHART m WHERE m.wDefMonID = TMONSTERCHART.wID OR m.wUpgMonID = TMONSTERCHART.wID);
GO

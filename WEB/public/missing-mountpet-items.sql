-- 006_titemchart_missing_mountpet_items
--
-- Adds the 0 mount/pet item(s) present in the client data (Game/Tcd/TItem.tcd)
-- but missing from dbo.TITEMCHART. TITEMCHART has 52 NOT-NULL columns and ~15 of them
-- do not exist in the client file, so each row is built by CLONING an existing sibling
-- of the same item type (bType) and overwriting the fields the client file provides
-- (same technique as 004_companion_missing_species.sql on TMONSTERCHART).
--
-- Inherited from the sibling (not present in TItem.tcd): fPrice, fPvPrice, bIsSell,
-- bEquipSkill, bUseItemKind/Count, bGrade, bDropLevel, dwSpeedInc, bItemCountry,
-- fRevision/fMRevision/fAtRate/fMAtRate, wItemProb_G, wWeight, bGroupID, bInitState, wExpandValue.
--
-- NOTE: TMapSvr/TWorldSvr load TITEMCHART once at startup - RESTART the servers after applying.
-- Idempotent: each insert is guarded by IF NOT EXISTS (target) AND EXISTS (sibling).
USE [TGame_gsp];
GO

SET NOCOUNT ON;
GO


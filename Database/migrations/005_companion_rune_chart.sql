-- Companion rune -> species mapping chart (canonical source) + repair of existing rune instances.
--
-- Companion runes (item template type IT_COMPANION = 22) all share the generic template useValue 31124,
-- so the species is per-INSTANCE data the server reads at creation (TMapSvr\CSHandler.cpp ~18893):
--     mon = (dwDuraMax != 0) ? dwDuraMax : ExtValue[IEV_COMPANION];   (ExtValue[4] == TITEMTABLE.dwTime5)
-- This build had nothing that supplied that value. TMapSvr now loads this table at startup and stamps
-- the species onto every rune instance at creation (SetItemAttr), so newly granted runes self-populate.
--
-- This migration owns the single source of truth (the chart) and also repairs any rune items that
-- already exist in a live DB (legacy instances created before the server fix).
--
-- Item-id -> companion monster-id is the stock EU companion-rune set (TItem.tcd type-22 runes matched to
-- TMon.tcd creatures). Species 31175/31176 are provided by migration 004 (runs first). The runes with no
-- creature of their own -- the generic "Companion Rune" (17013/19028) and "Bee Queen" (18451) -- map to
-- Suckling (31125).
--
-- Idempotent: table create guarded; rows MERGE'd; instance repair sets absolute values only where they differ.
USE [TGame_gsp];
GO

SET NOCOUNT ON;

IF OBJECT_ID('dbo.TCOMPANIONRUNECHART') IS NULL
    CREATE TABLE dbo.TCOMPANIONRUNECHART
    (
        wItemID SMALLINT NOT NULL PRIMARY KEY,   -- companion-rune item id
        wMonID  SMALLINT NOT NULL                -- companion monster (species) id
    );
GO

-- Seed / reconcile the mapping (canonical; TMapSvr reads the same table).
MERGE dbo.TCOMPANIONRUNECHART AS t
USING (VALUES
    (17013,31125),(18451,31125),(19019,31125),(19020,31127),(19021,31130),(19022,31131),
    (19023,31132),(19024,31126),(19025,31128),(19026,31129),(19027,31132),(19028,31125),
    (19029,31125),(19030,31125),(19031,31127),(19032,31127),(19033,31130),(19034,31130),
    (19035,31131),(19036,31131),(19037,31132),(19038,31132),(19039,31125),(19040,31127),
    (19041,31130),(19042,31131),(19043,31132),(19044,31552),(19045,31796),(19046,16059),
    (19047,31141),(19048,31736),(19049,31125),(19050,31125),(19051,31125),(19052,23001),
    (19053,31169),(19054,31172),(19055,31173),(19056,31174),(19057,31175),(19058,31176),
    (19059,31177),(19060,31178)
) AS s(wItemID, wMonID)
    ON t.wItemID = s.wItemID
WHEN NOT MATCHED BY TARGET THEN
    INSERT (wItemID, wMonID) VALUES (s.wItemID, s.wMonID)
WHEN MATCHED AND t.wMonID <> s.wMonID THEN
    UPDATE SET t.wMonID = s.wMonID;
GO

-- Repair existing rune instances from the chart: write the native species field (dwTime5) and clear
-- the dwDuraMax override so dwTime5 is authoritative. New runes are handled by the server at creation.
UPDATE t
   SET t.dwTime5   = c.wMonID,
       t.dwDuraMax = 0
FROM dbo.TITEMTABLE AS t
JOIN dbo.TCOMPANIONRUNECHART AS c ON c.wItemID = t.wItemID
WHERE t.dwTime5 <> c.wMonID OR t.dwDuraMax <> 0;
GO

-- Companion fix: add the two companion species missing from the server's monster chart.
--
-- Companion creation (TMapSvr OnCS_CREATECOMPANION_REQ, CSHandler.cpp) validates the rune's target
-- monster id against the in-memory monster table loaded from dbo.TMONSTERCHART:
--     itMon = m_mapTMONSTER.find(monId); if (itMon == end) return EC_NOERROR;  // silent no-op
-- Mon ids 31175 (Easter Squirrel) and 31176 (Humpty Dumpty) exist in the CLIENT data
-- (Game\Tcd\TMon.tcd, so they render correctly) but are ABSENT from TMONSTERCHART, while their
-- neighbours 31177/31178 are present. Result: those two runes pop the name dialog, then silently
-- do nothing on confirm. This adds the two rows so the server accepts them.
--
-- Fix = clone the working sibling row 31177 (Bouncing Bunny), changing only wID. Companions carry no
-- TMONATTRCHART row (31172-31178 have none), so TMONSTERCHART is the only table involved; stats come
-- from level scaling + TCOMPANIONBONUSCHART. The client renders each by its own TMon.tcd model.
--
-- NOTE: TMapSvr loads TMONSTERCHART once at startup, so the running map server must be RESTARTED
-- (e.g. `docker compose up`/restart) for these new species to take effect.
--
-- Idempotent: each insert is guarded by IF NOT EXISTS, and only runs if the 31177 template is present.
USE [TGame_gsp];
GO

SET NOCOUNT ON;
GO

-- 31175 Easter Squirrel
IF NOT EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = 31175)
   AND EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = 31177)
BEGIN
    SELECT * INTO #m31175 FROM dbo.TMONSTERCHART WHERE wID = 31177;
    UPDATE #m31175 SET wID = 31175;
    INSERT INTO dbo.TMONSTERCHART SELECT * FROM #m31175;
    DROP TABLE #m31175;
END
GO

-- 31176 Humpty Dumpty
IF NOT EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = 31176)
   AND EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = 31177)
BEGIN
    SELECT * INTO #m31176 FROM dbo.TMONSTERCHART WHERE wID = 31177;
    UPDATE #m31176 SET wID = 31176;
    INSERT INTO dbo.TMONSTERCHART SELECT * FROM #m31176;
    DROP TABLE #m31176;
END
GO

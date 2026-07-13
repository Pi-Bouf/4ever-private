-- 006_titemchart_missing_mountpet_items
--
-- Adds the 31 mount/pet item(s) present in the client data (Game/Tcd/TItem.tcd)
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

-- 25054  Blue Pearl Dragon  (clone of 25058, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25054)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25058)
BEGIN
    SELECT * INTO #t25054 FROM dbo.TITEMCHART WHERE wItemID = 25058;
    UPDATE #t25054 SET wItemID=25054, szNAME=N'Blue Pearl Dragon', bType=12, bKind=23, wAttrID=0, wUseValue=51, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t25054;
    DROP TABLE #t25054;
END
GO

-- 25055  Red Pearl Dragon  (clone of 25058, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25055)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25058)
BEGIN
    SELECT * INTO #t25055 FROM dbo.TITEMCHART WHERE wItemID = 25058;
    UPDATE #t25055 SET wItemID=25055, szNAME=N'Red Pearl Dragon', bType=12, bKind=23, wAttrID=0, wUseValue=52, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t25055;
    DROP TABLE #t25055;
END
GO

-- 25056  Pink Pearl Dragon  (clone of 25058, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25056)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25058)
BEGIN
    SELECT * INTO #t25056 FROM dbo.TITEMCHART WHERE wItemID = 25058;
    UPDATE #t25056 SET wItemID=25056, szNAME=N'Pink Pearl Dragon', bType=12, bKind=23, wAttrID=0, wUseValue=53, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t25056;
    DROP TABLE #t25056;
END
GO

-- 25057  Green Pearl Dragon  (clone of 25058, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25057)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 25058)
BEGIN
    SELECT * INTO #t25057 FROM dbo.TITEMCHART WHERE wItemID = 25058;
    UPDATE #t25057 SET wItemID=25057, szNAME=N'Green Pearl Dragon', bType=12, bKind=23, wAttrID=0, wUseValue=54, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t25057;
    DROP TABLE #t25057;
END
GO

-- 30236  Bouncing Bunny  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30236)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30236 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30236 SET wItemID=30236, szNAME=N'Bouncing Bunny', bType=12, bKind=23, wAttrID=0, wUseValue=31, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30236;
    DROP TABLE #t30236;
END
GO

-- 30237  Bouncing Bunny  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30237)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30237 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30237 SET wItemID=30237, szNAME=N'Bouncing Bunny', bType=12, bKind=23, wAttrID=0, wUseValue=31, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=64, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30237;
    DROP TABLE #t30237;
END
GO

-- 30238  Bouncing Bunny (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30238)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30238 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30238 SET wItemID=30238, szNAME=N'Bouncing Bunny (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=31, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30238;
    DROP TABLE #t30238;
END
GO

-- 30239  Spots  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30239)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30239 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30239 SET wItemID=30239, szNAME=N'Spots', bType=12, bKind=23, wAttrID=0, wUseValue=32, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30239;
    DROP TABLE #t30239;
END
GO

-- 30240  Spots  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30240)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30240 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30240 SET wItemID=30240, szNAME=N'Spots', bType=12, bKind=23, wAttrID=0, wUseValue=32, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=0, wUseTime=0, bUseType=64, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30240;
    DROP TABLE #t30240;
END
GO

-- 30241  Spots (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30241)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30241 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30241 SET wItemID=30241, szNAME=N'Spots (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=32, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30241;
    DROP TABLE #t30241;
END
GO

-- 30242  Black Nine-tailed Fox  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30242)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30242 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30242 SET wItemID=30242, szNAME=N'Black Nine-tailed Fox', bType=12, bKind=23, wAttrID=0, wUseValue=33, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30242;
    DROP TABLE #t30242;
END
GO

-- 30243  Black Nine-tailed Fox  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30243)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30243 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30243 SET wItemID=30243, szNAME=N'Black Nine-tailed Fox', bType=12, bKind=23, wAttrID=0, wUseValue=33, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30243;
    DROP TABLE #t30243;
END
GO

-- 30244  Black Nine-tailed Fox (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30244)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30244 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30244 SET wItemID=30244, szNAME=N'Black Nine-tailed Fox (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=33, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30244;
    DROP TABLE #t30244;
END
GO

-- 30245  Whitish green Nine-tailed Fox  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30245)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30245 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30245 SET wItemID=30245, szNAME=N'Whitish green Nine-tailed Fox', bType=12, bKind=23, wAttrID=0, wUseValue=34, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30245;
    DROP TABLE #t30245;
END
GO

-- 30246  Whitish green Nine-tailed Fox  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30246)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30246 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30246 SET wItemID=30246, szNAME=N'Whitish green Nine-tailed Fox', bType=12, bKind=23, wAttrID=0, wUseValue=34, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30246;
    DROP TABLE #t30246;
END
GO

-- 30247  Whitish green Nine-tailed Fox (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30247)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30247 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30247 SET wItemID=30247, szNAME=N'Whitish green Nine-tailed Fox (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=34, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=0, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30247;
    DROP TABLE #t30247;
END
GO

-- 30248  Rainbow Unicorn (30 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30248)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30248 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30248 SET wItemID=30248, szNAME=N'Rainbow Unicorn (30 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=35, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30248;
    DROP TABLE #t30248;
END
GO

-- 30249  Rainbow Unicorn  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30249)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30249 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30249 SET wItemID=30249, szNAME=N'Rainbow Unicorn', bType=12, bKind=23, wAttrID=0, wUseValue=35, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30249;
    DROP TABLE #t30249;
END
GO

-- 30250  Rainbow Unicorn (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30250)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30250 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30250 SET wItemID=30250, szNAME=N'Rainbow Unicorn (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=35, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30250;
    DROP TABLE #t30250;
END
GO

-- 30251  Blue Unicorn (30 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30251)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30251 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30251 SET wItemID=30251, szNAME=N'Blue Unicorn (30 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=36, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30251;
    DROP TABLE #t30251;
END
GO

-- 30252  Blue Unicorn  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30252)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30252 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30252 SET wItemID=30252, szNAME=N'Blue Unicorn', bType=12, bKind=23, wAttrID=0, wUseValue=36, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30252;
    DROP TABLE #t30252;
END
GO

-- 30253  Blue Unicorn (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30253)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30253 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30253 SET wItemID=30253, szNAME=N'Blue Unicorn (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=36, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30253;
    DROP TABLE #t30253;
END
GO

-- 30254  Pink Unicorn (30 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30254)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30254 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30254 SET wItemID=30254, szNAME=N'Pink Unicorn (30 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=37, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30254;
    DROP TABLE #t30254;
END
GO

-- 30255  Pink Unicorn  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30255)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30255 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30255 SET wItemID=30255, szNAME=N'Pink Unicorn', bType=12, bKind=23, wAttrID=0, wUseValue=37, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30255;
    DROP TABLE #t30255;
END
GO

-- 30256  Pink Unicorn (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30256)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30256 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30256 SET wItemID=30256, szNAME=N'Pink Unicorn (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=37, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30256;
    DROP TABLE #t30256;
END
GO

-- 30257  Egg Ride (30 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30257)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30257 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30257 SET wItemID=30257, szNAME=N'Egg Ride (30 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=38, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30257;
    DROP TABLE #t30257;
END
GO

-- 30258  Egg Ride  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30258)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30258 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30258 SET wItemID=30258, szNAME=N'Egg Ride', bType=12, bKind=23, wAttrID=0, wUseValue=38, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30258;
    DROP TABLE #t30258;
END
GO

-- 30259  Egg Ride (7 Days)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30259)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30259 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30259 SET wItemID=30259, szNAME=N'Egg Ride (7 Days)', bType=12, bKind=23, wAttrID=0, wUseValue=38, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30259;
    DROP TABLE #t30259;
END
GO

-- 30260  Polar Battle Bear  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30260)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30260 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30260 SET wItemID=30260, szNAME=N'Polar Battle Bear', bType=12, bKind=23, wAttrID=0, wUseValue=39, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=30, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30260;
    DROP TABLE #t30260;
END
GO

-- 30261  Polar Battle Bear (Permanent)  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30261)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30261 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30261 SET wItemID=30261, szNAME=N'Polar Battle Bear (Permanent)', bType=12, bKind=23, wAttrID=0, wUseValue=39, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=0, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30261;
    DROP TABLE #t30261;
END
GO

-- 30262  Polar Battle Bear  (clone of 30235, bType=12)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30262)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 30235)
BEGIN
    SELECT * INTO #t30262 FROM dbo.TITEMCHART WHERE wItemID = 30235;
    UPDATE #t30262 SET wItemID=30262, szNAME=N'Polar Battle Bear', bType=12, bKind=23, wAttrID=0, wUseValue=39, dwSlotID=0, dwClassID=63, bPrmSlotID=255, bSubSlotID=255, bLevel=1, bCanRepair=0, dwDuraMax=0, bRefineMax=0, bMinRange=0, bMaxRange=0, bStack=200, bSlotCount=0, bCanGamble=0, bGambleProb=0, bDestroyProb=0, bCanGrade=0, bCanMagic=0, bCanRare=0, wDelayGroupID=0, dwDelay=0, bIsSpecial=1, wUseTime=7, bUseType=66, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t30262;
    DROP TABLE #t30262;
END
GO

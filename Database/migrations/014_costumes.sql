-- 014_costumes
--
-- Adds the 15 costumes (bType=17) present in the client data (Game/Tcd/TItem.tcd) but missing from dbo.TITEMCHART:
--   * 7 ported from the official Gameforge FR client by Tools/OfficialPort (`port --apply`):
--     12063 Sublime Outfit, 12064 Snorkel, 12065/12080 Seasoned Beer Drinker + Hat, 12066/12081 Vampire + Mask,
--     12067 Christmas Panda;
--   * 8 that were already in the client without a chart row: 12045/12046 Dazzling Ensemble, 12047/12048 Rabbit Hat,
--     12049/12050 Floppy Ears Costume, 12055/12056 Chicken Hat.
-- Each row CLONES a sibling costume (12059 Hawaiian Headdress for hats, 12061 Hawaiian Clothing for outfits) for the
-- ~15 columns the client file does not carry, then overwrites the fields it does (same technique as 006).
--
-- NOTE: TMapSvr/TWorldSvr load TITEMCHART once at startup - RESTART the servers after applying.
-- Idempotent: each insert is guarded by IF NOT EXISTS (target) AND EXISTS (sibling).
USE [TGame_gsp];
GO

SET NOCOUNT ON;
GO

-- 12045  Dazzling Ensemble  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12045)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12045 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12045 SET wItemID=12045, szNAME=N'Dazzling Ensemble', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12045;
    DROP TABLE #t12045;
END
GO

-- 12046  Dazzling Ensemble  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12046)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12046 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12046 SET wItemID=12046, szNAME=N'Dazzling Ensemble', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12046;
    DROP TABLE #t12046;
END
GO

-- 12047  Rabbit Hat  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12047)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12047 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12047 SET wItemID=12047, szNAME=N'Rabbit Hat', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12047;
    DROP TABLE #t12047;
END
GO

-- 12048  Rabbit Hat  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12048)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12048 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12048 SET wItemID=12048, szNAME=N'Rabbit Hat', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12048;
    DROP TABLE #t12048;
END
GO

-- 12049  Floppy Ears Costume  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12049)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12049 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12049 SET wItemID=12049, szNAME=N'Floppy Ears Costume', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12049;
    DROP TABLE #t12049;
END
GO

-- 12050  Floppy Ears Costume  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12050)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12050 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12050 SET wItemID=12050, szNAME=N'Floppy Ears Costume', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=7, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=1;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12050;
    DROP TABLE #t12050;
END
GO

-- 12055  Chicken Hat  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12055)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12055 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12055 SET wItemID=12055, szNAME=N'Chicken Hat', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12055;
    DROP TABLE #t12055;
END
GO

-- 12056  Chicken Hat (Permanent)  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12056)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12056 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12056 SET wItemID=12056, szNAME=N'Chicken Hat (Permanent)', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12056;
    DROP TABLE #t12056;
END
GO

-- 12063  Sublime Outfit  (OFFICIAL port)  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12063)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12063 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12063 SET wItemID=12063, szNAME=N'Sublime Outfit', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12063;
    DROP TABLE #t12063;
END
GO

-- 12064  Snorkel  (OFFICIAL port)  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12064)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12064 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12064 SET wItemID=12064, szNAME=N'Snorkel', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12064;
    DROP TABLE #t12064;
END
GO

-- 12065  Seasoned Beer Drinker  (OFFICIAL port)  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12065)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12065 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12065 SET wItemID=12065, szNAME=N'Seasoned Beer Drinker', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12065;
    DROP TABLE #t12065;
END
GO

-- 12066  Vampire  (OFFICIAL port)  (clone of 12061)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12066)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12061)
BEGIN
    SELECT * INTO #t12066 FROM dbo.TITEMCHART WHERE wItemID = 12061;
    UPDATE #t12066 SET wItemID=12066, szNAME=N'Vampire', bType=17, bKind=89, wAttrID=0, wUseValue=0, dwSlotID=65536, dwClassID=63, bPrmSlotID=16, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12066;
    DROP TABLE #t12066;
END
GO

-- 12067  Christmas Panda  (OFFICIAL port)  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12067)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12067 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12067 SET wItemID=12067, szNAME=N'Christmas Panda', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12067;
    DROP TABLE #t12067;
END
GO

-- 12080  Seasoned Beer Drinker Hat  (OFFICIAL port)  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12080)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12080 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12080 SET wItemID=12080, szNAME=N'Seasoned Beer Drinker Hat', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12080;
    DROP TABLE #t12080;
END
GO

-- 12081  Vampire Mask  (OFFICIAL port)  (clone of 12059)
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12081)
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = 12059)
BEGIN
    SELECT * INTO #t12081 FROM dbo.TITEMCHART WHERE wItemID = 12059;
    UPDATE #t12081 SET wItemID=12081, szNAME=N'Vampire Mask', bType=17, bKind=88, wAttrID=0, wUseValue=0, dwSlotID=32768, dwClassID=63, bPrmSlotID=15, bSubSlotID=255, bLevel=1, bStack=1, bIsSell=5, bIsSpecial=1, wUseTime=0, bUseType=2, bCanWrap=0, dwCode=1048783, bCanColor=0;
    INSERT INTO dbo.TITEMCHART SELECT * FROM #t12081;
    DROP TABLE #t12081;
END
GO

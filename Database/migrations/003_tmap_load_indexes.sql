-- TMapSvr load speedup: indexes for the two N+1 load loops.
-- TMapSvr's [load] log spends ~36s in two loops (TMapSvr.cpp) that fire one "WHERE col = ?" query per
-- parent row. Each filtered column is a non-key column of a static chart table, so every call is a full
-- table scan. These nonclustered indexes turn each call into a seek (cf. CTBLSpawnPath, which bulk-loads
-- its whole table in 47ms). Read-only chart tables, so no write-penalty concern.
-- Idempotent: each CREATE is guarded against sys.indexes.
USE [TGame_gsp];
GO

-- Quest tree recursion (LoadQuestTemp -> 4 queries per quest node)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TQUESTCHART_dwParentID' AND object_id = OBJECT_ID('dbo.TQUESTCHART'))
    CREATE NONCLUSTERED INDEX IX_TQUESTCHART_dwParentID ON dbo.TQUESTCHART (dwParentID, bMain DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TQCONDITIONCHART_dwQuestID' AND object_id = OBJECT_ID('dbo.TQCONDITIONCHART'))
    CREATE NONCLUSTERED INDEX IX_TQCONDITIONCHART_dwQuestID ON dbo.TQCONDITIONCHART (dwQuestID, bConditionType DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TQREWARDCHART_dwQuestID' AND object_id = OBJECT_ID('dbo.TQREWARDCHART'))
    CREATE NONCLUSTERED INDEX IX_TQREWARDCHART_dwQuestID ON dbo.TQREWARDCHART (dwQuestID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TQUESTTERMCHART_dwQuestID' AND object_id = OBJECT_ID('dbo.TQUESTTERMCHART'))
    CREATE NONCLUSTERED INDEX IX_TQUESTTERMCHART_dwQuestID ON dbo.TQUESTTERMCHART (dwQuestID, dwID);
GO

-- Monster-spawn loop (per spawn row)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TMONSPAWNCHART_wPartyID' AND object_id = OBJECT_ID('dbo.TMONSPAWNCHART'))
    CREATE NONCLUSTERED INDEX IX_TMONSPAWNCHART_wPartyID ON dbo.TMONSPAWNCHART (wPartyID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TMAPMONCHART_wSpawnID' AND object_id = OBJECT_ID('dbo.TMAPMONCHART'))
    CREATE NONCLUSTERED INDEX IX_TMAPMONCHART_wSpawnID ON dbo.TMAPMONCHART (wSpawnID);
GO

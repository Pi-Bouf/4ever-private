-- 015_save_guild_stats_proc
--
-- The baseline has the guild stats table (dbo.TGUILDSTATSTABLE: dwGuildID, bSkillPoint, bLevel, dwExp) but not the proc the
-- world saves it with (C++ CSPSaveGuildStats, TWorldSvr/DBAccess.h: `{ CALL TSaveGuildStats(?,?,?,?) }` — guild, skill points,
-- level, exp). Without it every guild stat-exp gain (a fort or castle taken, a guild level-up) failed to save; in TWorldSvr.Net
-- the failure also cut the castle capture short. This creates it as an upsert.
--
-- Idempotent: CREATE OR ALTER.
USE [TGame_gsp];
GO

CREATE OR ALTER PROCEDURE [dbo].[TSaveGuildStats]
    @dwGuildID   INT,
    @bSkillPoint TINYINT,
    @bLevel      TINYINT,
    @dwExp       INT
AS
SET NOCOUNT ON;
UPDATE dbo.TGUILDSTATSTABLE SET bSkillPoint = @bSkillPoint, bLevel = @bLevel, dwExp = @dwExp WHERE dwGuildID = @dwGuildID;
IF (@@ROWCOUNT = 0)
    INSERT INTO dbo.TGUILDSTATSTABLE (dwGuildID, bSkillPoint, bLevel, dwExp) VALUES (@dwGuildID, @bSkillPoint, @bLevel, @dwExp);
GO

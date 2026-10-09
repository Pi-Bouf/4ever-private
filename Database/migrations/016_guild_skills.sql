-- 016_guild_skills
--
-- The guild skills the client shows (GuildSkillsDlg.cpp: chief 3000/3002/3301, vice-chief 3001/3300/3302, member
-- 3600/3602/3603/3605 — all in TSKILLCHART) had no server data in the baseline:
--   * dbo.TGUILDSKILLCHART existed but was empty (C++ CTBLGuildSkillChart: bType, wSkillID — the duty a skill needs:
--     0 member, 1 vice-chief, 2 chief);
--   * the guild's own (officer) skills table and the save proc were missing (C++ CTBLGuildSkillDuty reads
--     TGUILDMASTERSKILLTABLE by wSkillID + dwGuildID; CSPSaveGuildSkill is `{CALL TSaveGuildSkill(?,?,?,?,?,?)}` —
--     char, guild, type, skill, level, end time). A member skill (type 0) is the character's (TGUILDMEMBERSKILLTABLE, by
--     dwCharID); an officer skill (type 1/2) is the guild's.
--
-- Idempotent: the chart rows are merged, the table created only if missing, the proc CREATE OR ALTER.
USE [TGame_gsp];
GO

MERGE dbo.TGUILDSKILLCHART AS t
USING (VALUES (2, 3000), (2, 3002), (2, 3301), (1, 3001), (1, 3300), (1, 3302), (0, 3600), (0, 3602), (0, 3603), (0, 3605))
    AS s (bType, wSkillID)
ON t.wSkillID = s.wSkillID
WHEN MATCHED THEN UPDATE SET bType = s.bType
WHEN NOT MATCHED THEN INSERT (bType, wSkillID) VALUES (s.bType, s.wSkillID);
GO

IF OBJECT_ID('dbo.TGUILDMASTERSKILLTABLE') IS NULL
    CREATE TABLE dbo.TGUILDMASTERSKILLTABLE (
        dwGuildID INT           NOT NULL,
        wSkillID  SMALLINT      NOT NULL,
        bLevel    TINYINT       NOT NULL,
        tEndTime  SMALLDATETIME NOT NULL,
        CONSTRAINT PK_TGUILDMASTERSKILLTABLE PRIMARY KEY (dwGuildID, wSkillID)
    );
GO

CREATE OR ALTER PROCEDURE [dbo].[TSaveGuildSkill]
    @dwCharID  INT,
    @dwGuildID INT,
    @bType     TINYINT,
    @wSkillID  SMALLINT,
    @bLevel    TINYINT,
    @tEndTime  SMALLDATETIME
AS
SET NOCOUNT ON;
IF (@bType = 0)
BEGIN
    UPDATE dbo.TGUILDMEMBERSKILLTABLE SET bLevel = @bLevel, tEndTime = @tEndTime WHERE dwCharID = @dwCharID AND wSkillID = @wSkillID;
    IF (@@ROWCOUNT = 0)
        INSERT INTO dbo.TGUILDMEMBERSKILLTABLE (wSkillID, bLevel, tEndTime, dwCharID) VALUES (@wSkillID, @bLevel, @tEndTime, @dwCharID);
END
ELSE
BEGIN
    UPDATE dbo.TGUILDMASTERSKILLTABLE SET bLevel = @bLevel, tEndTime = @tEndTime WHERE dwGuildID = @dwGuildID AND wSkillID = @wSkillID;
    IF (@@ROWCOUNT = 0)
        INSERT INTO dbo.TGUILDMASTERSKILLTABLE (dwGuildID, wSkillID, bLevel, tEndTime) VALUES (@dwGuildID, @wSkillID, @bLevel, @tEndTime);
END
GO

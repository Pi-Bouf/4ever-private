-- Evocate Monster (skill 618) calls the sorcerer's tamed monster as a main summon that stays until it dies.
--
-- A summon's lifetime is the skill's dwDuration (+ the SCT_INCLIFTTIME passive); 0 means no timer (every source's
-- CheckTimeRecallMon only expires a summon whose m_dwDurationTick is set). The 5.0 baseline gives Evocate 5000 ms
-- because 5.0 turned it into a short strike (three copies around an enemy). The port keeps the old self-cast, which
-- needs the ritual-style 0 (Dark/Holy Ritual 623/628 are 0 too).
-- Idempotent.
USE [TGame_gsp];
GO

SET NOCOUNT ON;

UPDATE dbo.TSKILLCHART SET dwDuration = 0, dwDurationInc = 0
WHERE wID = 618 AND (dwDuration <> 0 OR dwDurationInc <> 0);
GO

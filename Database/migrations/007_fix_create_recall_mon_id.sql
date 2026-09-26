-- Fix TCreateRecallMon: it relied on TRECALLMONTABLE.dwID being an IDENTITY column (SET @dwMonID = @@IDENTITY)
-- and omitted dwID from its INSERT, but in this DB dwID is a plain NOT NULL int. Recall ids are normally
-- allocated by the world server (GenRecallID, seeded from TGetRecallID = MAX(dwID)) and written explicitly by
-- TSaveRecallMon, so no identity exists.
--
-- Effect of the bug: TCreateChar calls TCreateRecallMon for every class that has a TSTARTRECALL row (class 5,
-- the summoner, all three countries). The INSERT failed with error 515 ("Cannot insert the value NULL into
-- column 'dwID'"); the character itself was already written, but the error surfaced to the login server,
-- which threw before sending CS_CREATECHAR_ACK -> the client froze on the create screen.
--
-- Fix: allocate dwID as MAX(dwID)+1 under an update/range lock so concurrent creations can't collide.
-- Idempotent: CREATE OR ALTER.
USE [TGame_gsp];
GO

CREATE OR ALTER PROCEDURE [dbo].[TCreateRecallMon]
@dwMonID int output,
@dwCharID int,
@wMonTemp smallint,
@wPetID smallint,
@dwATTR int,
@bLevel tinyint,
@dwHP int,
@dwMP int,
@bSkillLevel tinyint,
@wPosX smallint,
@wPosY smallint,
@wPosZ smallint,
@dwTime int
AS

BEGIN TRAN CREATERECALLMON

SELECT @dwMonID = ISNULL(MAX(dwID), 0) + 1 FROM TRECALLMONTABLE WITH (UPDLOCK, HOLDLOCK)

INSERT INTO TRECALLMONTABLE(dwOwnerID, dwID, wMonID, wPetID, dwATTR, bLevel, dwHP, dwMP, bSkillLevel, wPosX, wPosY, wPosZ, dwTime, bEffect)
	VALUES(@dwCharID, @dwMonID, @wMonTemp, @wPetID, @dwATTR, @bLevel, @dwHP, @dwMP, @bSkillLevel, @wPosX, @wPosY, @wPosZ, @dwTime, 0)

COMMIT TRAN CREATERECALLMON
GO

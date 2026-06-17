-- Seed a local-dev account: test / test123
-- TLogin compares the client-sent password directly against TACCOUNT_PW.szPasswd (no server-side
-- hashing), and the 4Story client sends the password as an uppercase SHA1 hex string — so we store
-- UPPER(SHA1('test123')). dwUserID is an IDENTITY column. Idempotent: only seeds if absent.
USE [TGlobal_gsp];
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TACCOUNT_PW WHERE szUserID = 'test')
BEGIN
    INSERT INTO dbo.TACCOUNT_PW (szUserID, szPasswd)
    VALUES ('test', UPPER(CONVERT(VARCHAR(40), HASHBYTES('SHA1', 'test123'), 2)));
    PRINT 'Seeded account: test';
END
ELSE
    PRINT 'Account test already exists - skipping.';
GO

-- Local-network topology config.
--   1. Point every machine address at the LAN IP so clients reach the map/world cluster.
--      TRoute hands TIPADDR.szIPAddr back to the client on CS_START.
--   2. Set the Lapiris game-DB credential (used by the C++ servers' TGAME_GSP ODBC DSN) to the dev
--      SA login. The password comes from .env via the sqlcmd variable $(SA_PASSWORD) — it is NOT
--      hardcoded here (the runner passes -v SA_PASSWORD=...).
-- Plain UPDATEs, so re-running is harmless.
USE [TGlobal_gsp];
GO

UPDATE dbo.TIPADDR
   SET szIPAddr = '192.168.1.37';

UPDATE dbo.TGROUP
   SET szUserID = 'sa',
       szPasswd = '$(SA_PASSWORD)'
 WHERE bGroupID = 1;   -- Lapiris
GO

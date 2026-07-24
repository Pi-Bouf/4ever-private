@echo off
title TBot - WATCH (spawns next to Pittt, 5 min)
echo Starting TBot - it will spawn at Pittt's spot (map 0 region 772) on channel 1 for 5 minutes.
echo.
"E:\Projects\4Story\Araz-4ever\Servers\TBot\bin\Debug\net10.0\TBot.exe" ^
  --Bot:Account=%1 ^
  --Bot:Password=Botpass123 ^
  --Bot:CreateAccount=true ^
  "--Bot:GlobalConnectionString=Server=localhost,11433;Database=TGlobal_gsp;User ID=sa;Password=Dev_Local_4Story!2026;TrustServerCertificate=True;Encrypt=False" ^
  "--Bot:GameConnectionString=Server=localhost,11433;Database=TGame_gsp;User ID=sa;Password=Dev_Local_4Story!2026;TrustServerCertificate=True;Encrypt=False" ^
  --Bot:MatchPositionOfCharId=2 ^
  --Bot:CharName=%2 ^
  --Bot:CharSlot=0 ^
  --Bot:Class=3 --Bot:Race=0 --Bot:Country=4 --Bot:Sex=0 --Bot:Hair=4 --Bot:Face=5 ^
  --Bot:LevelOption=1 ^
  --Bot:Channel=1 ^
  --Bot:NoCrypt=false --Bot:MapNoCrypt=false ^
  --Bot:MoveRadius=6 --Bot:MoveStep=1 --Bot:MoveTickMs=150 --Bot:MoveDurationSec=300
echo.
echo === Bot finished (disconnected). Press any key to close. ===
pause >nul

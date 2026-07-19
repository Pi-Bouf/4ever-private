@echo off
title TBot - live demo
echo Starting TBot (docker .NET login + C++ map, crypto on, newbie-zone spawn)...
echo.
"E:\Projects\4Story\Araz-4ever\Servers\TBot\bin\Debug\net10.0\TBot.exe" ^
  --Bot:Account=%1 ^
  --Bot:Password=Botpass123 ^
  --Bot:CreateAccount=true ^
  "--Bot:GlobalConnectionString=Server=localhost,11433;Database=TGlobal_gsp;User ID=sa;Password=Dev_Local_4Story!2026;TrustServerCertificate=True;Encrypt=False" ^
  --Bot:CharName=%2 ^
  --Bot:CharSlot=0 ^
  --Bot:Class=3 --Bot:Race=0 --Bot:Country=4 --Bot:Sex=0 --Bot:Hair=4 --Bot:Face=5 ^
  --Bot:Channel=1 ^
  --Bot:NoCrypt=false --Bot:MapNoCrypt=false ^
  --Bot:MoveRadius=30 --Bot:MoveDurationSec=60
echo.
echo === Bot finished (disconnected). Press any key to close this window. ===
pause >nul

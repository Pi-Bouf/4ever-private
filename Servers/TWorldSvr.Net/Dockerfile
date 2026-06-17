# Multi-stage build: restore + publish with the .NET 10 SDK, run on the slim ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TWorld.Protocol/TWorld.Protocol.csproj TWorld.Protocol/
COPY TWorld.Data/TWorld.Data.csproj TWorld.Data/
COPY TWorld.Server/TWorld.Server.csproj TWorld.Server/
RUN dotnet restore TWorld.Server/TWorld.Server.csproj

COPY TWorld.Protocol/ TWorld.Protocol/
COPY TWorld.Data/ TWorld.Data/
COPY TWorld.Server/ TWorld.Server/
RUN dotnet publish TWorld.Server/TWorld.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 3815
ENTRYPOINT ["dotnet", "TWorld.Server.dll"]

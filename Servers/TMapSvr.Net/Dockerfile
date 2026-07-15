# Multi-stage build: restore + publish with the .NET 10 SDK, run on the slim ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TMap.Protocol/TMap.Protocol.csproj TMap.Protocol/
COPY TMap.Data/TMap.Data.csproj TMap.Data/
COPY TMap.Server/TMap.Server.csproj TMap.Server/
RUN dotnet restore TMap.Server/TMap.Server.csproj

COPY TMap.Protocol/ TMap.Protocol/
COPY TMap.Data/ TMap.Data/
COPY TMap.Server/ TMap.Server/
RUN dotnet publish TMap.Server/TMap.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 5816
ENTRYPOINT ["dotnet", "TMap.Server.dll"]

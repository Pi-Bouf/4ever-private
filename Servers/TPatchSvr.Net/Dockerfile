# Multi-stage build: restore + publish with the .NET 10 SDK, run on the slim ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TPatch.Protocol/TPatch.Protocol.csproj TPatch.Protocol/
COPY TPatch.Data/TPatch.Data.csproj TPatch.Data/
COPY TPatch.Server/TPatch.Server.csproj TPatch.Server/
RUN dotnet restore TPatch.Server/TPatch.Server.csproj

COPY TPatch.Protocol/ TPatch.Protocol/
COPY TPatch.Data/ TPatch.Data/
COPY TPatch.Server/ TPatch.Server/
RUN dotnet publish TPatch.Server/TPatch.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 3715
ENTRYPOINT ["dotnet", "TPatch.Server.dll"]

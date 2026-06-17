# Multi-stage build: restore + publish with the .NET 10 SDK, run on the slim ASP.NET runtime.
# The .slnx solution format is not understood by older tooling, so we build the server project directly.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TLogin.Protocol/TLogin.Protocol.csproj TLogin.Protocol/
COPY TLogin.Data/TLogin.Data.csproj TLogin.Data/
COPY TLogin.Server/TLogin.Server.csproj TLogin.Server/
RUN dotnet restore TLogin.Server/TLogin.Server.csproj

COPY TLogin.Protocol/ TLogin.Protocol/
COPY TLogin.Data/ TLogin.Data/
COPY TLogin.Server/ TLogin.Server/
RUN dotnet publish TLogin.Server/TLogin.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 4816
ENTRYPOINT ["dotnet", "TLogin.Server.dll"]

# Multi-stage build: restore + publish with the .NET 10 SDK, run on the slim ASP.NET runtime.
# The .slnx solution format is not understood by older tooling, so we build the server project directly.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TControl.Protocol/TControl.Protocol.csproj TControl.Protocol/
COPY TControl.Data/TControl.Data.csproj TControl.Data/
COPY TControl.Server/TControl.Server.csproj TControl.Server/
RUN dotnet restore TControl.Server/TControl.Server.csproj

COPY TControl.Protocol/ TControl.Protocol/
COPY TControl.Data/ TControl.Data/
COPY TControl.Server/ TControl.Server/
RUN dotnet publish TControl.Server/TControl.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 3615
ENTRYPOINT ["dotnet", "TControl.Server.dll"]

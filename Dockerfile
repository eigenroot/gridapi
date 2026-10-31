# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY nuget.config ./
COPY src/Grid.Api/Grid.Api.csproj src/Grid.Api/
RUN dotnet restore src/Grid.Api/Grid.Api.csproj
COPY src/Grid.Api/ src/Grid.Api/
RUN dotnet publish src/Grid.Api/Grid.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
RUN mkdir -p /app/App_Data/media && chown -R app:app /app/App_Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Grid.Api.dll"]

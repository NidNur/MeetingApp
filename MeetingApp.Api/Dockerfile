ARG DOTNET_TAG=10.0-noble

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
WORKDIR /src

COPY global.json ./
COPY MeetingApp.Model/MeetingApp.Model.csproj MeetingApp.Model/
COPY MeetingApp.Context/MeetingApp.Context.csproj MeetingApp.Context/
COPY MeetingApp.Api/MeetingApp.Api.csproj MeetingApp.Api/
RUN dotnet restore MeetingApp.Api/MeetingApp.Api.csproj

COPY MeetingApp.Model/ MeetingApp.Model/
COPY MeetingApp.Context/ MeetingApp.Context/
COPY MeetingApp.Api/ MeetingApp.Api/
RUN dotnet publish MeetingApp.Api/MeetingApp.Api.csproj \
    --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_TAG} AS final
WORKDIR /app

USER root
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/Storage/ProfileImages /app/Storage/MeetingDocuments \
    && chown -R app:app /app/Storage

COPY --from=build /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
USER app

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=6 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready > /dev/null || exit 1

ENTRYPOINT ["dotnet", "MeetingApp.Api.dll"]

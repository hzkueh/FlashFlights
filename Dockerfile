# One parameterised Dockerfile for all four services: they differ only in which
# project is published, so four near-identical files would just drift apart.
# docker-compose passes PROJECT per service.
ARG PROJECT

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/

RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJECT
WORKDIR /app

# curl is here purely so compose can healthcheck the service's own /health.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    SERVICE_DLL=${PROJECT}.dll
EXPOSE 8080

# Shell form so SERVICE_DLL expands; exec so the app is PID 1 and gets SIGTERM.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet $SERVICE_DLL"]

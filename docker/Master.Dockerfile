FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/DepPilot.Contracts/ src/DepPilot.Contracts/
COPY src/DepPilot.Master/ src/DepPilot.Master/
COPY playbooks/ playbooks/
RUN dotnet publish src/DepPilot.Master -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DepPilot.Master.dll"]

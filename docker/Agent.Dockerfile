FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/DepPilot.Contracts/ src/DepPilot.Contracts/
COPY src/DepPilot.Agent/ src/DepPilot.Agent/
RUN dotnet publish src/DepPilot.Agent -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv ca-certificates \
    && python3 -m venv /opt/ansible \
    && /opt/ansible/bin/pip install --no-cache-dir ansible-core==2.19.3 requests==2.32.5 \
    && rm -rf /var/lib/apt/lists/*
ENV PATH="/opt/ansible/bin:${PATH}"
COPY playbooks/requirements.yml /tmp/requirements.yml
RUN ansible-galaxy collection install -r /tmp/requirements.yml -p /opt/ansible/collections
ENV ANSIBLE_COLLECTIONS_PATH=/opt/ansible/collections
ENV ANSIBLE_HOME=/tmp/ansible ANSIBLE_REMOTE_TEMP=/tmp/ansible/remote
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/state && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "DepPilot.Agent.dll"]

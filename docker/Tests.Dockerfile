FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tests
WORKDIR /source
COPY Directory.Build.props global.json DepPilot.sln ./
COPY src/ src/
COPY tests/ tests/
COPY playbooks/ playbooks/
COPY packages/ packages/
RUN dotnet test DepPilot.sln -c Release --logger trx --results-directory /results

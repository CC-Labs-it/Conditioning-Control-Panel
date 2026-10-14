# Codex cloud environment setup script (runs with internet before the agent starts)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export PATH=$HOME/.dotnet:$PATH
echo 'export PATH=$HOME/.dotnet:$PATH' >> ~/.bashrc
git fetch origin avalonia-port/goongame-core-c parity/trunk || true
dotnet restore CCP.Avalonia/CCP.Avalonia.csproj
dotnet restore Tests/CCP.Core.Tests/CCP.Core.Tests.csproj
dotnet restore Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj

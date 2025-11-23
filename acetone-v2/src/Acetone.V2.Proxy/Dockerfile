# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy solution and project files first for better caching
COPY ["Acetone.V2.sln", "./"]
COPY ["src/Acetone.V2.Core/Acetone.V2.Core.csproj", "src/Acetone.V2.Core/"]
COPY ["src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj", "src/Acetone.V2.Proxy/"]
# Copy test projects to allow running tests in CI if needed, but usually we skip them in final image build
# For now, just restore the main projects
RUN dotnet restore "src/Acetone.V2.Proxy/Acetone.V2.Proxy.csproj"

# Copy the rest of the source code
COPY . .

# Build and Publish
WORKDIR "/src/src/Acetone.V2.Proxy"
RUN dotnet publish "Acetone.V2.Proxy.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# Configure environment
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0 
# Enable diagnostics=0 for security in production, but might hinder debugging. 
# For fintech, we prefer logs and metrics over attaching debuggers.

# Run as non-root user (already default in chiseled images, but good to be explicit if we weren't)
# Chiseled images don't have a shell or root user.

EXPOSE 8080
EXPOSE 9090

ENTRYPOINT ["dotnet", "Acetone.V2.Proxy.dll"]

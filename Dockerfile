# Use the official .NET SDK image for building the application
# SDK 10 matches the local/VS toolchain; the app still targets net9.0 and runs on the 9.0 runtime below.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

# Copy the project files
COPY VTMF.Settings VTMF.Settings

COPY VTMF.SQLInstaller VTMF.SQLInstaller

COPY VTMF.Settings.Core VTMF.Settings.Core

# Restore dependencies
RUN dotnet restore "VTMF.Settings/VTMF.Settings.csproj"

# Build and publish the application
RUN dotnet publish "VTMF.Settings/VTMF.Settings.csproj" -c Release -o /app/publish

# Use the official .NET runtime image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=builder /app/publish .
COPY VTMF.Settings/install.xml /app/install.xml

# Expose the port your application runs on
EXPOSE 80

# Set the entry point for the container
ENTRYPOINT ["dotnet", "VTMF.Settings.dll"]

@echo off
dotnet run --project "%~dp0build\_build.csproj" --no-launch-profile -- %*

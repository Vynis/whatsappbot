# =========================================================================
# Multi-stage Dockerfile para XerifeBot.Api (.NET 8 Web API)
# =========================================================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj e restaurar dependências
COPY ["XerifeBot.Api.csproj", "./"]
RUN dotnet restore "XerifeBot.Api.csproj"

# Copiar todo o código-fonte e publicar em modo Release
COPY . .
RUN dotnet publish "XerifeBot.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Imagem final de execução (Runtime leve ASP.NET Core 8)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5001

ENV ASPNETCORE_URLS=http://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "XerifeBot.Api.dll"]

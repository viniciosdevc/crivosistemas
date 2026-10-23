# Imagem da API. Também usada pelo serviço `migrate` (`--migrate`) e pelo comando `--seed-admin`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Crivosistemas.slnx ./
COPY src/backend/Crivosistemas.Domain/Crivosistemas.Domain.csproj src/backend/Crivosistemas.Domain/
COPY src/backend/Crivosistemas.Infrastructure/Crivosistemas.Infrastructure.csproj src/backend/Crivosistemas.Infrastructure/
COPY src/backend/Crivosistemas.Api/Crivosistemas.Api.csproj src/backend/Crivosistemas.Api/
RUN dotnet restore src/backend/Crivosistemas.Api/Crivosistemas.Api.csproj
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/Crivosistemas.Api/Crivosistemas.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
  CMD ["dotnet", "Crivosistemas.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Crivosistemas.Api.dll"]

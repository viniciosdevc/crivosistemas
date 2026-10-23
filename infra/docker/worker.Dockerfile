FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/backend/Crivosistemas.Domain/Crivosistemas.Domain.csproj src/backend/Crivosistemas.Domain/
COPY src/backend/Crivosistemas.Infrastructure/Crivosistemas.Infrastructure.csproj src/backend/Crivosistemas.Infrastructure/
COPY src/workers/Crivosistemas.Worker/Crivosistemas.Worker.csproj src/workers/Crivosistemas.Worker/
RUN dotnet restore src/workers/Crivosistemas.Worker/Crivosistemas.Worker.csproj
COPY src/backend/Crivosistemas.Domain/ src/backend/Crivosistemas.Domain/
COPY src/backend/Crivosistemas.Infrastructure/ src/backend/Crivosistemas.Infrastructure/
COPY src/workers/Crivosistemas.Worker/ src/workers/Crivosistemas.Worker/
RUN dotnet publish src/workers/Crivosistemas.Worker/Crivosistemas.Worker.csproj -c Release -o /app --no-restore

# A Infrastructure referencia o framework compartilhado ASP.NET Core, então a imagem de runtime simples não consegue iniciar o worker.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Crivosistemas.Worker.dll"]

# Imagem para publicar a API (Render, Railway, Fly.io, Azure App Service…).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kognit.slnx ./
COPY src/Kognit.Dominio/Kognit.Dominio.csproj src/Kognit.Dominio/
COPY src/Kognit.Api/Kognit.Api.csproj src/Kognit.Api/
RUN dotnet restore src/Kognit.Api/Kognit.Api.csproj
COPY src/ src/
RUN dotnet publish src/Kognit.Api/Kognit.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Com Supabase (BancoDeDados__Provedor=Postgres) o volume não é usado; com SQLite, o arquivo fica em /data.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Padrao="Data Source=/data/kognit.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kognit.Api.dll"]

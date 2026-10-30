# Imagem para publicar a API (Render, Railway, Fly.io, Azure App Service…).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CaixaClaro.slnx ./
COPY src/CaixaClaro.Dominio/CaixaClaro.Dominio.csproj src/CaixaClaro.Dominio/
COPY src/CaixaClaro.Api/CaixaClaro.Api.csproj src/CaixaClaro.Api/
RUN dotnet restore src/CaixaClaro.Api/CaixaClaro.Api.csproj
COPY src/ src/
RUN dotnet publish src/CaixaClaro.Api/CaixaClaro.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# O banco SQLite fica num volume (/data) para não se perder a cada deploy.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Padrao="Data Source=/data/caixaclaro.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "CaixaClaro.Api.dll"]

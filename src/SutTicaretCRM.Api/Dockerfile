# Web arayüzü + API tek imajda. Derleme: repo kökünden  docker compose --profile app build
FROM node:22-alpine AS web
WORKDIR /web
COPY src/SutTicaretCRM.Web/package.json src/SutTicaretCRM.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/SutTicaretCRM.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SutTicaretCRM.sln ./
COPY src/ src/
RUN dotnet publish src/SutTicaretCRM.Api/SutTicaretCRM.Api.csproj -c Release -o /out /p:UseAppHost=false
COPY --from=web /web/dist /out/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
# Yüklenen dosyalar için yazılabilir klasör; uygulama root olmayan kullanıcıyla çalışır
RUN mkdir -p /data/storage && chown -R $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 Storage__Root=/data/storage
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["dotnet", "SutTicaretCRM.Api.dll", "--healthcheck"] 
ENTRYPOINT ["dotnet", "SutTicaretCRM.Api.dll"]

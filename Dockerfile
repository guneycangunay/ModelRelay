# syntax=docker/dockerfile:1.7

FROM node:22-alpine AS ui-build
WORKDIR /ui
COPY ui/package.json ./
RUN npm install --no-audit --no-fund
COPY ui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ModelRelay.slnx
RUN dotnet publish src/ModelRelay.Api/ModelRelay.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
RUN addgroup -S modelrelay && adduser -S modelrelay -G modelrelay
COPY --from=build /app ./
COPY --from=ui-build /ui/dist ./wwwroot
USER modelrelay
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ModelRelay.Api.dll"]

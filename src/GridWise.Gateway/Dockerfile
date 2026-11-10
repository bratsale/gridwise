FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/GridWise.Gateway/GridWise.Gateway.csproj src/GridWise.Gateway/
RUN dotnet restore src/GridWise.Gateway/GridWise.Gateway.csproj
COPY src/GridWise.Gateway/ src/GridWise.Gateway/
RUN dotnet publish src/GridWise.Gateway/GridWise.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Gateway.dll"]

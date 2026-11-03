FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/GridWise.Catalog/GridWise.Catalog.csproj src/GridWise.Catalog/
RUN dotnet restore src/GridWise.Catalog/GridWise.Catalog.csproj
COPY src/GridWise.Catalog/ src/GridWise.Catalog/
RUN dotnet publish src/GridWise.Catalog/GridWise.Catalog.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Catalog.dll"]

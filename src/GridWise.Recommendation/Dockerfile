FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/GridWise.Recommendation/GridWise.Recommendation.csproj src/GridWise.Recommendation/
COPY src/GridWise.Pricing/GridWise.Pricing.csproj src/GridWise.Pricing/
RUN dotnet restore src/GridWise.Recommendation/GridWise.Recommendation.csproj
COPY src/GridWise.Recommendation/ src/GridWise.Recommendation/
COPY src/GridWise.Pricing/ src/GridWise.Pricing/
RUN dotnet publish src/GridWise.Recommendation/GridWise.Recommendation.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Recommendation.dll"]

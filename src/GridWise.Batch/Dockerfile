FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/GridWise.Batch/GridWise.Batch.csproj src/GridWise.Batch/
RUN dotnet restore src/GridWise.Batch/GridWise.Batch.csproj
COPY src/GridWise.Batch/ src/GridWise.Batch/
RUN dotnet publish src/GridWise.Batch/GridWise.Batch.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Batch.dll"]

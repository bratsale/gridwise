# The build runs on the build machine's platform and publishes for the target one (TARGETARCH, set by BuildKit), so an
# arm64 image needs no emulation. One runtime identifier keeps only that platform's librdkafka, not those of every OS.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY src/GridWise.Batch/GridWise.Batch.csproj src/GridWise.Batch/
RUN RID=linux-musl-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
 && echo "$RID" > /rid \
 && dotnet restore src/GridWise.Batch/GridWise.Batch.csproj -r "$RID"
COPY src/GridWise.Batch/ src/GridWise.Batch/
RUN dotnet publish src/GridWise.Batch/GridWise.Batch.csproj -c Release -o /app --no-restore -r "$(cat /rid)" --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Batch.dll"]

# The build runs on the build machine's platform and publishes for the target one (TARGETARCH, set by BuildKit), so an
# arm64 image needs no emulation. One runtime identifier keeps only that platform's librdkafka, not those of every OS.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY src/GridWise.Catalog/GridWise.Catalog.csproj src/GridWise.Catalog/
RUN RID=linux-musl-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
 && echo "$RID" > /rid \
 && dotnet restore src/GridWise.Catalog/GridWise.Catalog.csproj -r "$RID"
COPY src/GridWise.Catalog/ src/GridWise.Catalog/
RUN dotnet publish src/GridWise.Catalog/GridWise.Catalog.csproj -c Release -o /app --no-restore -r "$(cat /rid)" --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# librdkafka's arm64 Alpine build needs Cyrus SASL (libsasl2.so.3); the x64 build has it built in.
RUN apk add --no-cache libsasl
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Catalog.dll"]

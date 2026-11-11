# The build runs on the build machine's platform and publishes for the target one (TARGETARCH, set by BuildKit), so an
# arm64 image needs no emulation. One runtime identifier keeps only that platform's librdkafka, not those of every OS.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY src/GridWise.Recommendation/GridWise.Recommendation.csproj src/GridWise.Recommendation/
COPY src/GridWise.Pricing/GridWise.Pricing.csproj src/GridWise.Pricing/
RUN RID=linux-musl-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
 && echo "$RID" > /rid \
 && dotnet restore src/GridWise.Recommendation/GridWise.Recommendation.csproj -r "$RID"
COPY src/GridWise.Recommendation/ src/GridWise.Recommendation/
COPY src/GridWise.Pricing/ src/GridWise.Pricing/
RUN dotnet publish src/GridWise.Recommendation/GridWise.Recommendation.csproj -c Release -o /app --no-restore -r "$(cat /rid)" --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridWise.Recommendation.dll"]

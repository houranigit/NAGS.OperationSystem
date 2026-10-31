# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS source
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Modules/ src/Modules/
COPY src/Host/OperationsSystem.Api/ src/Host/OperationsSystem.Api/
COPY src/Host/OperationsSystem.Blazor/ src/Host/OperationsSystem.Blazor/
COPY src/Host/OperationsSystem.Mobile/app/src/main/res/drawable-nodpi/nags_logo.png src/Host/OperationsSystem.Mobile/app/src/main/res/drawable-nodpi/
COPY docs/user-manual/release/nags-operations-field-guide.html docs/user-manual/release/

FROM source AS api-build
# Honor the existing publish requirement without retaining the credential in an image layer.
RUN --mount=type=secret,id=firebase_admin,target=/src/src/Host/OperationsSystem.Api/App_Data/firebase-admin.json,required=true \
    dotnet publish src/Host/OperationsSystem.Api/OperationsSystem.Api.csproj -c Release -o /out /p:UseAppHost=false \
    && rm -f /out/App_Data/firebase-admin.json \
    && find /src -path '*/bin/*' -name firebase-admin.json -delete

FROM source AS portal-build
RUN dotnet publish src/Host/OperationsSystem.Blazor/OperationsSystem.Blazor/OperationsSystem.Blazor.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM runtime AS api
RUN mkdir -p /data/storage /data/keys && chown -R app:app /data
COPY --from=api-build /out/ ./
USER app
ENTRYPOINT ["dotnet", "OperationsSystem.Api.dll"]

FROM runtime AS portal
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
COPY --from=portal-build /out/ ./
USER app
ENTRYPOINT ["dotnet", "OperationsSystem.Blazor.dll"]

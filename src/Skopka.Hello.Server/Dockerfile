FROM mcr.microsoft.com/dotnet/sdk:10.0.101@sha256:5504edd1267dd4deab3443f960cfab219249c8bd935fbcc358f1c24aeae23fe0 AS build
ARG RELEASE_VERSION
WORKDIR /src

COPY NuGet.Config Directory.Build.props Directory.Packages.props ./
COPY src/Skopka.Hello/Skopka.Hello.csproj src/Skopka.Hello/
COPY src/Skopka.Hello.Admin/Skopka.Hello.Admin.csproj src/Skopka.Hello.Admin/
COPY src/Skopka.Hello.AuthorizationServer/Skopka.Hello.AuthorizationServer.csproj src/Skopka.Hello.AuthorizationServer/
COPY src/Skopka.Hello.Endpoints/Skopka.Hello.Endpoints.csproj src/Skopka.Hello.Endpoints/
COPY src/Skopka.Hello.Oidc/Skopka.Hello.Oidc.csproj src/Skopka.Hello.Oidc/
COPY src/Skopka.Hello.UI/Skopka.Hello.UI.csproj src/Skopka.Hello.UI/
COPY src/Skopka.Hello.Server/Skopka.Hello.Server.csproj src/Skopka.Hello.Server/
RUN dotnet restore src/Skopka.Hello.Server/Skopka.Hello.Server.csproj \
    --configfile NuGet.Config

COPY src/Skopka.Hello src/Skopka.Hello
COPY src/Skopka.Hello.Admin src/Skopka.Hello.Admin
COPY src/Skopka.Hello.AuthorizationServer src/Skopka.Hello.AuthorizationServer
COPY src/Skopka.Hello.Endpoints src/Skopka.Hello.Endpoints
COPY src/Skopka.Hello.Oidc src/Skopka.Hello.Oidc
COPY src/Skopka.Hello.UI src/Skopka.Hello.UI
COPY src/Skopka.Hello.Server src/Skopka.Hello.Server
RUN dotnet publish src/Skopka.Hello.Server/Skopka.Hello.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    ${RELEASE_VERSION:+-p:Version=${RELEASE_VERSION}}

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:ccdca44cd4f256d50187f920dc8ccc2a9ea7a8a4597ac1d51e08fddb2e3b3205 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/skopka-hello/keys \
        /var/lib/skopka-hello/customization \
    && chown -R app:app /var/lib/skopka-hello
ENV SkopkaHello__DataProtection__KeyPath=/var/lib/skopka-hello/keys
ENV SkopkaHello__Customization__CssFilePath=/var/lib/skopka-hello/customization/custom.css
VOLUME ["/var/lib/skopka-hello/keys", "/var/lib/skopka-hello/customization"]
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=10 \
    CMD ["dotnet", "Skopka.Hello.Server.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Skopka.Hello.Server.dll"]

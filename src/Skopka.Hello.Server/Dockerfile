FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config Directory.Build.props Directory.Packages.props ./
COPY src/Skopka.Hello/Skopka.Hello.csproj src/Skopka.Hello/
COPY src/Skopka.Hello.Endpoints/Skopka.Hello.Endpoints.csproj src/Skopka.Hello.Endpoints/
COPY src/Skopka.Hello.UI/Skopka.Hello.UI.csproj src/Skopka.Hello.UI/
COPY src/Skopka.Hello.Server/Skopka.Hello.Server.csproj src/Skopka.Hello.Server/
RUN dotnet restore src/Skopka.Hello.Server/Skopka.Hello.Server.csproj \
    --configfile NuGet.Config

COPY src/Skopka.Hello src/Skopka.Hello
COPY src/Skopka.Hello.Endpoints src/Skopka.Hello.Endpoints
COPY src/Skopka.Hello.UI src/Skopka.Hello.UI
COPY src/Skopka.Hello.Server src/Skopka.Hello.Server
RUN dotnet publish src/Skopka.Hello.Server/Skopka.Hello.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
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

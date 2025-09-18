FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Skopka.Hello/Skopka.Hello.csproj", "src/Skopka.Hello/"]
RUN dotnet restore "src/Skopka.Hello/Skopka.Hello.csproj"
COPY . .
WORKDIR "/src/src/Skopka.Hello"
RUN dotnet build "./Skopka.Hello.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Skopka.Hello.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Skopka.Hello.dll"]

# Сборка AuthServer. Контекст сборки — корень репозитория (нужны проектные ссылки).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только проектные файлы — для кэширования restore.
COPY AlexWoW.slnx ./
COPY src/AlexWoW.Common/AlexWoW.Common.csproj src/AlexWoW.Common/
COPY src/AlexWoW.Cryptography/AlexWoW.Cryptography.csproj src/AlexWoW.Cryptography/
COPY src/AlexWoW.Database/AlexWoW.Database.csproj src/AlexWoW.Database/
COPY src/AlexWoW.AuthServer/AlexWoW.AuthServer.csproj src/AlexWoW.AuthServer/
RUN dotnet restore src/AlexWoW.AuthServer/AlexWoW.AuthServer.csproj

# Затем исходники и публикация.
COPY src/ src/
RUN dotnet publish src/AlexWoW.AuthServer/AlexWoW.AuthServer.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 3724
ENTRYPOINT ["dotnet", "AlexWoW.AuthServer.dll"]

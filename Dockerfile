# Runtime-only образ. Компиляция выполняется ЛОКАЛЬНО (dotnet publish),
# на сервер копируются готовые бинарники в каталог ./publish.
# На сервере НЕТ ни SDK, ни restore, ни компиляции — только COPY в рантайм.
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY publish/ ./
EXPOSE 3724
ENTRYPOINT ["dotnet", "AlexWoW.AuthServer.dll"]

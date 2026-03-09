FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/TelegramChainBot.csproj ./src/
RUN dotnet restore ./src/TelegramChainBot.csproj

COPY src ./src
RUN dotnet publish ./src/TelegramChainBot.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./
COPY webapp ./webapp
RUN mkdir -p /app/data

ENTRYPOINT ["dotnet", "TelegramChainBot.dll"]

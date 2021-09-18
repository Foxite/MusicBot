FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["IkIheMusicBot.csproj", "./"]
RUN dotnet restore "IkIheMusicBot.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "IkIheMusicBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IkIheMusicBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IkIheMusicBot.dll"]

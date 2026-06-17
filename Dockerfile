FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/HackerNews.Api/HackerNews.Api.csproj", "src/HackerNews.Api/"]
COPY ["src/HackerNews.Core/HackerNews.Core.csproj", "src/HackerNews.Core/"]
COPY ["src/HackerNews.Infrastructure/HackerNews.Infrastructure.csproj", "src/HackerNews.Infrastructure/"]
RUN dotnet restore "src/HackerNews.Api/HackerNews.Api.csproj"

COPY . .
RUN dotnet publish "src/HackerNews.Api/HackerNews.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HackerNews.Api.dll"]

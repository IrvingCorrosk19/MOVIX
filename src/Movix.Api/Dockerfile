FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Movix.Api/Movix.Api.csproj", "Movix.Api/"]
COPY ["src/Movix.Application/Movix.Application.csproj", "Movix.Application/"]
COPY ["src/Movix.Infrastructure/Movix.Infrastructure.csproj", "Movix.Infrastructure/"]
COPY ["src/Movix.Domain/Movix.Domain.csproj", "Movix.Domain/"]
RUN dotnet restore "Movix.Api/Movix.Api.csproj"
COPY src/ .
WORKDIR "/src/Movix.Api"
RUN dotnet build "Movix.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Movix.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Movix.Api.dll"]

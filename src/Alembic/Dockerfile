FROM mcr.microsoft.com/dotnet/aspnet:5.0-alpine AS base
ADD ./Alembic/VERSION .
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /src
COPY ["Alembic/Alembic.csproj", "Alembic/"]
COPY ["Alembic.Docker/Alembic.Docker.csproj", "Alembic.Docker/"]
COPY ["Alembic.Reporting/Alembic.Reporting.csproj", "Alembic.Reporting/"]
COPY ["Alembic.Common/Alembic.Common.csproj", "Alembic.Common/"]
RUN dotnet restore "Alembic/Alembic.csproj"
COPY . .
WORKDIR "/src/Alembic"
RUN dotnet build "Alembic.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Alembic.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Alembic.dll"]
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY BoekenAfspraak.Api/*.csproj ./BoekenAfspraak.Api/
RUN dotnet restore ./BoekenAfspraak.Api/BoekenAfspraak.Api.csproj
COPY BoekenAfspraak.Api/. ./BoekenAfspraak.Api/
RUN dotnet publish ./BoekenAfspraak.Api/BoekenAfspraak.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# tzdata is required for TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV DATA_DIR=/data
EXPOSE 8080


ENTRYPOINT ["dotnet", "BoekenAfspraak.Api.dll"]

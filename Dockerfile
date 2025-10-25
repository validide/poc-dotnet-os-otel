FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src/api

COPY src/api/api.csproj .
RUN dotnet restore

COPY src/api/. .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5233
ENV ASPNETCORE_URLS=http://+:5233

ENTRYPOINT ["dotnet", "api.dll"]
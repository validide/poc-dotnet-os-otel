FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file
COPY api.csproj .
RUN dotnet restore

# Copy source files
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5233
ENV ASPNETCORE_URLS=http://+:5233

ENTRYPOINT ["dotnet", "api.dll"]

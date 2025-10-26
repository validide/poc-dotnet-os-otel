FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file
COPY storage-api.csproj .
RUN dotnet restore

# Copy source files
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5234
ENV ASPNETCORE_URLS=http://+:5234

ENTRYPOINT ["dotnet", "storage-api.dll"]

# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VioletStudio.sln ./
COPY VioletStudio.Web/VioletStudio.Web.csproj VioletStudio.Web/
RUN dotnet restore VioletStudio.Web/VioletStudio.Web.csproj

COPY . .
RUN dotnet publish VioletStudio.Web/VioletStudio.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "VioletStudio.Web.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["LoadDemoApi.Gateway.csproj", "."]
RUN dotnet restore "LoadDemoApi.Gateway.csproj"
COPY . .
RUN dotnet build "LoadDemoApi.Gateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "LoadDemoApi.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LoadDemoApi.Gateway.dll"]

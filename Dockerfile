FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Bluestaq.Notes.slnx global.json ./
COPY src/Bluestaq.Notes.Api/Bluestaq.Notes.Api.csproj src/Bluestaq.Notes.Api/
RUN dotnet restore src/Bluestaq.Notes.Api/Bluestaq.Notes.Api.csproj
COPY src/Bluestaq.Notes.Api/ src/Bluestaq.Notes.Api/
RUN dotnet publish src/Bluestaq.Notes.Api/Bluestaq.Notes.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
RUN mkdir /app/data && chown "$APP_UID:$APP_UID" /app/data
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Notes="Data Source=/app/data/notes.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bluestaq.Notes.Api.dll"]

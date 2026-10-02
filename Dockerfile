FROM node:24-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/Workout.Api/Workout.Api.csproj backend/Workout.Api/
RUN dotnet restore backend/Workout.Api/Workout.Api.csproj
COPY backend/Workout.Api/ backend/Workout.Api/
RUN dotnet publish backend/Workout.Api/Workout.Api.csproj -c Release --no-restore -o /out
COPY --from=frontend /src/frontend/dist /out/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys && chown app:app /keys
WORKDIR /app
COPY --from=backend /out .
RUN chmod -R a+rX /app/wwwroot
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workout.Api.dll"]

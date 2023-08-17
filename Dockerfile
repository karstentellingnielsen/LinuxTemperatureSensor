FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["LinuxTemperatureSensor.csproj", "."]
RUN dotnet restore "./LinuxTemperatureSensor.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "LinuxTemperatureSensor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "LinuxTemperatureSensor.csproj" -c Release -o /app/publish /p:UseAppHost=false
RUN ls -R /app


FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine as module
ARG EXE_DIR=.
ENV MODULE_NAME "LinuxTemperatureSensor.dll"
WORKDIR /app
COPY --from=publish /app/publish .
RUN ls -R /app
COPY $EXE_DIR/ ./

#Test definitiosn only, comment out for real installation
#RUN apk add lm-sensors lm-sensors-detect
#RUN echo i2c-dev >> /etc/modules-load.d/i2c.conf
#RUN modprobe i2c-dev
#ENV IS_DOCKER_ONLY=true
##########################################################

# Add an unprivileged user account for running the module
#RUN adduser -Ds /bin/sh moduleuser 
#USER moduleuser

RUN apk add lm-sensors lm-sensors-detect

CMD echo "$(date --utc +"[%Y-%m-%d %H:%M:%S %:z]"): Starting Module" && \
    exec /usr/bin/dotnet LinuxTemperatureSensor.dll

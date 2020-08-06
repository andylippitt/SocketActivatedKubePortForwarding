FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
WORKDIR /app

# copy everything else and build
COPY ./ ./
RUN dotnet publish SocketActivatedKubePortForwarding.csproj -c Release -o build --runtime linux-musl-x64 -p:PublishSelfContained=true -p:PublishSingleFile=true

# build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine
WORKDIR /app
COPY --from=build /app/build/SocketActivatedKubePortForwarding /app/SocketActivatedKubePortForwarding

RUN apk add --no-cache \
	aws-cli \
	libstdc++ \
	libintl

ENTRYPOINT ["/app/SocketActivatedKubePortForwarding", "ipany"]

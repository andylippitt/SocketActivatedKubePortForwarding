FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
WORKDIR /app
COPY ./ ./
RUN dotnet publish SocketActivatedKubePortForwarding.csproj \
	-c Release \
	-o build \
	--runtime linux-musl-x64 \
	-p:PublishSelfContained=true \
	-p:PublishSingleFile=true

# build runtime image
FROM alpine
WORKDIR /app
COPY --from=build /app/build/SocketActivatedKubePortForwarding /app/SocketActivatedKubePortForwarding

RUN apk add --no-cache \
	aws-cli \
	libstdc++ \
	libintl \
	icu

# (optional) aws eks requires aws cli for authentication
# ~/.aws can be mapped in as appropriate
RUN apk add --no-cache \
	aws-cli

# 'ipany' indicates to bind to all interfaces, not just localhost (0.0.0.0 instead of 127.0.0.1)
ENTRYPOINT ["/app/SocketActivatedKubePortForwarding", "ipany"]

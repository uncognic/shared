FROM eclipse-temurin:21-jdk-alpine AS build
WORKDIR /app
COPY gradle gradle
COPY gradlew .
COPY settings.gradle .
COPY build.gradle .
COPY src src
RUN ./gradlew bootJar --no-daemon

FROM eclipse-temurin:21-jre-alpine AS final
WORKDIR /app
RUN apk add --no-cache su-exec && \
    addgroup -S appgroup && adduser -S appuser -G appgroup
COPY --from=build /app/build/libs/*.jar app.jar
ARG VERSION=dev
ENV APP_VERSION=$VERSION
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "mkdir -p /app/shared/logs && chown -R appuser:appgroup /app/shared && exec su-exec appuser java -jar /app/app.jar"]

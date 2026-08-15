# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:9.0-noble@sha256:840c88158a49d65ab59451369988a0db6b420ab3ea7948a5e261c56a4ab3404d AS dotnet

FROM docker.io/library/gradle:8.13-jdk21@sha256:67b8c4bfd2b064e58a7307e2da1fc3881bc03ecc7a57cf61d8b570a02ebfaea2 AS gradle

FROM ghcr.io/cirruslabs/android-sdk:35@sha256:c724009e305b4607157287624033ab97f319af44c244bfc9f73b6293f3bb01b9

ENV DOTNET_ROOT=/usr/share/dotnet \
    GRADLE_HOME=/opt/gradle \
    GRADLE_OPTS="-Dorg.gradle.daemon=false -Dorg.gradle.vfs.watch=false" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="/root/.dotnet/tools:/usr/share/dotnet:/opt/gradle/bin:${PATH}"

COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
COPY --from=gradle /opt/gradle /opt/gradle

RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && yes | sdkmanager "ndk;28.1.13356709" \
    && mkdir -p /root/Android \
    && ln -s /opt/android-sdk-linux /root/Android/Sdk \
    && dotnet tool install csharpier --tool-path /root/.dotnet/tools --version 1.3.0

RUN ln -s /usr/bin/python3 /usr/local/bin/python

ENTRYPOINT ["/bin/bash"]
CMD ["/src/docker/build-apk.sh"]

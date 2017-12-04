# Build stage of the IoP-PS image, pulls a .NET SDK and compiles the source
# Do not use directly, see architecture dependent files for more information
FROM microsoft/dotnet:2.0.0-sdk AS build

COPY src /src
WORKDIR /src/ProfileServer

# Linux nuget requires those files to be present
# TODO: this is probably my bug if you know how to do
# it better pls contribute
RUN for i in ../Iop*; do ln -s `pwd`/NuGet.Config $i/; done

# e_sqlite3.so missing on ARM
RUN dotnet add package SQLitePCLRaw.lib.e_sqlite3.linux --version 1.1.8-pre20170717084758

RUN dotnet restore --configfile NuGet.Config
RUN dotnet publish -c Release -r linux-arm -o /build
RUN dotnet ef database update
RUN cp ProfileServer.db /build


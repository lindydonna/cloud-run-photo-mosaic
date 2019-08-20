FROM microsoft/dotnet:sdk AS build-env

RUN apt-get update && apt-get install -y libfontconfig1

WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

FROM microsoft/dotnet:aspnetcore-runtime
RUN apt-get update && apt-get install -y libfontconfig1

WORKDIR /app
COPY --from=build-env /app/out .

ENTRYPOINT [ "dotnet", "PhotoMosaicService.dll" ]

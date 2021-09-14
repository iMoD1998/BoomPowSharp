FROM mcr.microsoft.com/dotnet/sdk:3.1.412-alpine3.13 AS build-env
WORKDIR /src
COPY . ./
RUN cd BoomPowSharp && dotnet restore
RUN dotnet publish -c Release -o out -r alpine-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:PublishSingleFile=true -p:PublishReadyToRun=true
RUN ls out

FROM mcr.microsoft.com/dotnet/runtime:3.1.18-alpine3.13 AS run-env
WORKDIR /app
COPY --from=build-env /src/out .
COPY --from=build-env /src/run.sh /app/run.sh
RUN chmod +x /app/run.sh

ENV WORKER_URL="http://127.0.0.1:20000"
ENV PAYOUT_ADDRESS="ban_1ncpdt1tbusi9n4c7pg6tqycgn4oxrnz5stug1iqyurorhwbc9gptrsmxkop"

ENTRYPOINT ["/app/run.sh"]

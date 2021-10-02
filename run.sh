#!/usr/bin/env sh

WORKER_URL=${WORKER_URL:-http://127.0.0.1:20000}
PAYOUT_ADDRESS=${PAYOUT_ADDRESS:-ban_1ncpdt1tbusi9n4c7pg6tqycgn4oxrnz5stug1iqyurorhwbc9gptrsmxkop}
MIN_DIFFICULTY=${MIN_DIFFICULTY:-0}

./BoomPowSharp --worker-url $WORKER_URL --payout $PAYOUT_ADDRESS -d $MIN_DIFFICULTY
#!/bin/bash
# Captures the LookDev scene from its reference views: lookdev_views.sh <engine bin dir> <out prefix>
BIN="$1"; OUT="$2"
shot() { (cd "$BIN" && timeout 180 ./Engine.exe --scene Scenes/LookDev.obsc --shot "$OUT$1.png" --frames ${FRAMES:-120} --size 1280x720 --camera "$2" $EXTRA); }
shot overview "0,-14,3.2,0,0,0.8"
shot metals "1.8,-4.2,1.4,2.8,0,0.5"
shot room "10.5,-1.0,1.7,17,0.5,1.0"
shot doorway "4,-1,2.0,12,0,1.2"
shot glass "-7.5,-3.0,1.5,-11.5,0.3,1.0"
shot checker "-4,0.3,1.4,-4,4,1.3"
shot mirror "-5.8,13.5,1.8,-8,7.5,1.1"
EXTRA="$EXTRA --play" shot mirror_play "-7.3,11.2,1.62,-8.3,7.5,1.1"

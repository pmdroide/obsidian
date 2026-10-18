#!/bin/bash
# Look-dev captures of a set of scenes: lookdev.sh <engine bin dir> <out prefix> [scene...]
BIN="$1"; OUT="$2"; shift 2
SCENES="${@:-AnimationTest GodRayTest CollisionTest WeatherTest FoliageTest AutoExposureTest}"
for s in $SCENES; do
  (cd "$BIN" && timeout 180 ./Engine.exe --scene "Scenes/$s.obsc" --shot "$OUT$s.png" --frames ${FRAMES:-120} --size 1280x720 $EXTRA)
done

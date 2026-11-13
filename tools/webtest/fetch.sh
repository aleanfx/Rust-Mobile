#!/bin/bash
# Downloads the latest web build and serves it on :8080.
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
rm -rf "$DIR/site" && mkdir -p "$DIR/site"
curl -sSfL -o "$DIR/oxfall-web.zip" "https://github.com/aleanfx/Rust-Mobile/releases/download/web-latest/oxfall-web.zip"
python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$DIR/oxfall-web.zip" "$DIR/site"
ls -la "$DIR/site" "$DIR/site/Build"
pkill -f "http.server 8080" || true
(cd "$DIR/site" && nohup python3 -m http.server 8080 >/dev/null 2>&1 &)
sleep 1; curl -sS -o /dev/null -w "server %{http_code}\n" http://localhost:8080/

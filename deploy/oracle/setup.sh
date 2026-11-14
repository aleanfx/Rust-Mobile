#!/usr/bin/env bash
# Oxfall game server on a fresh Ubuntu machine (made for Oracle Cloud's Always Free VMs, Ampere
# or AMD; any Ubuntu 22.04/24.04 server works). Run once, as root:
#
#   curl -fsSL https://raw.githubusercontent.com/aleanfx/Rust-Mobile/claude/project-thread-v0it3s/deploy/oracle/setup.sh | sudo bash
#
# It installs .NET 8 and Caddy, builds the server from the repository, runs it as a service that
# restarts on its own (and so starts the new island at each weekly wipe), and puts Caddy in front
# with a free HTTPS name for the machine's address (<ip>.sslip.io), so the web build (served over
# HTTPS) and the phones can connect with wss://. Run it again to update to the latest code.
set -euo pipefail

REPO="https://github.com/aleanfx/Rust-Mobile.git"
BRANCH="${OXFALL_BRANCH:-claude/project-thread-v0it3s}"
SRC=/opt/oxfall/src
APP=/opt/oxfall/app
DATA=/var/lib/oxfall

if [ "$(id -u)" -ne 0 ]; then echo "Run as root (sudo)." >&2; exit 1; fi
export DEBIAN_FRONTEND=noninteractive

echo "== Packages"
apt-get update -q
apt-get install -y -q git curl ca-certificates gnupg debian-keyring debian-archive-keyring apt-transport-https iptables-persistent
apt-get install -y -q dotnet-sdk-8.0

if ! command -v caddy >/dev/null; then
  echo "== Caddy"
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -q
  apt-get install -y -q caddy
fi

echo "== Code ($BRANCH)"
if [ -d "$SRC/.git" ]; then
  git -C "$SRC" fetch -q --depth 1 origin "$BRANCH"
  git -C "$SRC" reset -q --hard FETCH_HEAD
else
  rm -rf "$SRC"
  # Only the server and the rules it shares with the game, not the art.
  git clone -q --depth 1 --filter=blob:none --sparse --branch "$BRANCH" "$REPO" "$SRC"
  git -C "$SRC" sparse-checkout set server Assets/Oxfall/Scripts/Core deploy
fi
echo "   $(git -C "$SRC" log -1 --format='%h %s')"

echo "== Build"
dotnet publish "$SRC/server/Oxfall.Server" -c Release -o "$APP.new" --nologo -v q
rm -rf "$APP.old"
[ -d "$APP" ] && mv "$APP" "$APP.old"
mv "$APP.new" "$APP"

echo "== Service"
id oxfall >/dev/null 2>&1 || useradd --system --home "$DATA" --shell /usr/sbin/nologin oxfall
mkdir -p "$DATA"
chown oxfall:oxfall "$DATA"
cat > /etc/systemd/system/oxfall.service <<EOF
[Unit]
Description=Oxfall game server
After=network-online.target
Wants=network-online.target

[Service]
User=oxfall
WorkingDirectory=$DATA
Environment=PORT=7777
Environment=OXFALL_SEED=weekly
Environment=OXFALL_SAVE=$DATA/oxfall-world.sav
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
ExecStart=/usr/bin/dotnet $APP/Oxfall.Server.dll
# Stops itself at each weekly wipe: started again at once on the new island.
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable -q oxfall
systemctl restart oxfall

echo "== HTTPS"
IP="$(curl -fsS https://api.ipify.org || curl -fsS https://ifconfig.me)"
HOST="${OXFALL_HOST:-${IP//./-}.sslip.io}"
cat > /etc/caddy/Caddyfile <<EOF
# Oxfall: HTTPS (and so wss://) in front of the game server.
$HOST {
    reverse_proxy 127.0.0.1:7777
}
EOF
# Oracle's Ubuntu images let in nothing but SSH: open the web ports (kept across reboots).
for port in 80 443; do
  iptables -C INPUT -p tcp -m state --state NEW --dport "$port" -j ACCEPT 2>/dev/null \
    || iptables -I INPUT 1 -p tcp -m state --state NEW --dport "$port" -j ACCEPT
done
netfilter-persistent save >/dev/null 2>&1 || true
systemctl enable -q caddy
systemctl restart caddy

sleep 3
echo
if curl -fsS http://127.0.0.1:7777/ >/dev/null; then
  echo "Oxfall server running: $(curl -fsS http://127.0.0.1:7777/)"
else
  echo "The server did not answer yet; see: journalctl -u oxfall -n 50" >&2
fi
echo
echo "Game address (give it to the game):  wss://$HOST/ws"
echo "Check in a browser:                  https://$HOST/"
echo "(If the browser cannot reach it, open ports 80 and 443 in the Oracle console: see deploy/oracle/LEEME.md.)"
echo "Update later with the same command. Logs: journalctl -u oxfall -f"

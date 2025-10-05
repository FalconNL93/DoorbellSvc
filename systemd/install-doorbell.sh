#!/usr/bin/env bash
set -euo pipefail

# ===== Config =====
USER="doorbell"
GROUP="doorbell"
AUDIO_GROUP="audio"
HOME_DIR="/var/lib/doorbell"
SERVICE_FILE_SRC="$(dirname "$0")/doorbelld.service"
SERVICE_FILE_DEST="/etc/systemd/system/doorbelld.service"
SOUNDS_DIR="$HOME_DIR/sounds"
CACHE_DIR="$HOME_DIR/cache"
SHELL_PATH="/usr/sbin/nologin"
PKGS=(alsa-utils ffmpeg)

# ===== Helpers =====
have_cmd() { command -v "$1" >/dev/null 2>&1; }
have_pkg() { dpkg -s "$1" >/dev/null 2>&1; }

ensure_group() {
  local g="$1"
  if ! getent group "$g" >/dev/null; then
    groupadd "$g"
    echo "Created group: $g"
  fi
}

ensure_user() {
  local u="$1" g="$2" home="$3" shell="$4"
  if ! id -u "$u" >/dev/null 2>&1; then
    adduser --system --home "$home" --group --shell "$shell" "$u"
    echo "Created system user: $u (home=$home, shell=$shell, group=$g)"
  else
    local cur_shell
    cur_shell="$(getent passwd "$u" | cut -d: -f7 || true)"
    if [[ "$cur_shell" != "$shell" ]] && grep -Fxq "$shell" /etc/shells; then
      chsh -s "$shell" "$u" || true
    fi
  fi
}

ensure_user_in_group() {
  local u="$1" g="$2"
  if ! id -nG "$u" | tr ' ' '\n' | grep -Fxq "$g"; then
    usermod -aG "$g" "$u"
    echo "Added $u to group $g"
  fi
}

ensure_dir() {
  local path="$1" owner="$2" group="$3" mode="$4"
  install -d -o "$owner" -g "$group" -m "$mode" "$path"
}

ensure_packages() {
  local -a missing=()
  for p in "${PKGS[@]}"; do
    have_pkg "$p" || missing+=("$p")
  done
  if ((${#missing[@]})); then
    apt-get update -y
    apt-get install -y "${missing[@]}"
  fi
}

# ===== Run =====

# 1) service user & groups
ensure_group "$GROUP"
ensure_user "$USER" "$GROUP" "$HOME_DIR" "$SHELL_PATH"
ensure_user_in_group "$USER" "$AUDIO_GROUP"

# 2) directories and permissions
ensure_dir "$HOME_DIR" "$USER" "$GROUP" 0755
ensure_dir "$SOUNDS_DIR" root "$GROUP" 0750
ensure_dir "$CACHE_DIR" "$USER" "$GROUP" 0700

# 3) packages
ensure_packages

# 4) install systemd service file
install -m 644 "$SERVICE_FILE_SRC" "$SERVICE_FILE_DEST"
systemctl daemon-reload
systemctl enable --now doorbelld.service

echo "\nSetup complete:\n- User: $USER (shell: $SHELL_PATH, home: $HOME_DIR)\n- Groups: $(id -nG "$USER")\n- Sounds dir: $SOUNDS_DIR (root:$GROUP, 750)\n- Cache dir:  $CACHE_DIR  ($USER:$GROUP, 700)\n- Packages: ${PKGS[*]} installed/verified\n- Systemd service: $SERVICE_FILE_DEST enabled and started\n"

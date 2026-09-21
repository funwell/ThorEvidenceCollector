#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PYTHON=${PYTHON:-python3}
VENV="$ROOT/.venv"

if ! command -v "$PYTHON" >/dev/null 2>&1; then
  echo "未找到 python3。请先安装 Python 3。" >&2
  exit 2
fi

if [ ! -x "$VENV/bin/python" ]; then
  "$PYTHON" -m venv "$VENV"
  "$VENV/bin/python" -m pip install --disable-pip-version-check -r "$ROOT/mac/requirements.txt"
fi

exec "$VENV/bin/python" "$ROOT/mac/thor_nvshell_collect.py" "$@"

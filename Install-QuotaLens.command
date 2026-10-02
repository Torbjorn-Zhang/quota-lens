#!/bin/bash
# Double-click in Finder (from a cloned repository) to build, install and start Quota Lens.
cd "$(dirname "$0")" || exit 1
bash ./install-mac.sh "$@"
status=$?
echo
read -n 1 -s -r -p "按任意键关闭此窗口…"
exit $status

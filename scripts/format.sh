#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
case "${1:-format}" in
  format) format_args=(format --no-cache) ;;
  check) format_args=(check) ;;
  *) printf 'Usage: %s [format|check]\n' "$0" >&2; exit 2 ;;
esac
dotnet csharpier "${format_args[@]}" src FrogSmashersRebuilt.slnx

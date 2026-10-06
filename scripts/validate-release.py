#!/usr/bin/env python3
"""Resolve the commit of a successful release build and reject conflicting tags."""

import argparse
import json
import os
import re
import subprocess
from urllib.parse import quote


def github_json(repository, path):
    result = subprocess.run(
        ["gh", "api", f"repos/{repository}/{path}"], check=True, capture_output=True, text=True
    )
    return json.loads(result.stdout)


def validate_release(repository, run_id, tag):
    if not re.fullmatch(r"[1-9][0-9]*", run_id):
        raise ValueError("Build run ID must be the number from the build run URL.")
    valid_tag = subprocess.run(["git", "check-ref-format", f"refs/tags/{tag}"], capture_output=True)
    if tag.startswith("-") or valid_tag.returncode:
        raise ValueError("Release tag is not a valid Git tag name.")

    run = github_json(repository, f"actions/runs/{run_id}")
    if run["path"] != ".github/workflows/build.yml":
        raise ValueError("Select a run of the Build workflow.")
    if run["status"] != "completed" or run["conclusion"] != "success":
        raise ValueError("The selected build must have completed successfully.")
    if run["event"] not in ("push", "workflow_dispatch"):
        raise ValueError("Select a push or manual build; pull requests build a temporary merge commit.")
    if run["head_repository"]["full_name"].lower() != repository.lower():
        raise ValueError("The selected build must come from this repository.")
    commit = run["head_sha"]
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("The build did not provide a valid commit SHA.")

    refs = github_json(repository, f"git/matching-refs/tags/{quote(tag, safe='')}")
    existing = next((ref for ref in refs if ref["ref"] == f"refs/tags/{tag}"), None)
    if existing is not None:
        target = existing["object"]
        while target["type"] == "tag":
            target = github_json(repository, f"git/tags/{target['sha']}")["object"]
        if target["type"] != "commit" or target["sha"] != commit:
            raise ValueError("The release tag already points to a different commit.")
    return commit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_id")
    parser.add_argument("tag")
    args = parser.parse_args()
    try:
        print(validate_release(os.environ["GITHUB_REPOSITORY"], args.run_id, args.tag))
    except ValueError as error:
        raise SystemExit(str(error))
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.stderr.strip() or str(error))


if __name__ == "__main__":
    main()

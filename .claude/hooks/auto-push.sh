#!/usr/bin/env bash
# Stop hook: when Claude finishes a turn, commit whatever changed in the project and push it to GitHub
# (origin = github.com/Rolbubx/test, branch main). Does nothing when there is nothing new.
cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}" || exit 0
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 0

if [ -n "$(git status --porcelain)" ]; then
    files=$(git status --porcelain | wc -l | tr -d ' ')
    git add -A
    git commit -q -m "Update $(date '+%Y-%m-%d %H:%M') ($files file(s))

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" || exit 0
fi

# Push new commits (also ones made earlier that were not pushed yet).
if [ -n "$(git log --oneline @{u}..HEAD 2>/dev/null)" ]; then
    GIT_TERMINAL_PROMPT=0 git push -q 2>&1 | tail -3
fi
exit 0

# Règles du projet

- **Toujours pousser sur `main`** (`git push origin HEAD:main`) une fois les changements commités, sans passer par une
  branche ni une PR. Jamais de force-push : si `main` a avancé, `git fetch origin main && git merge origin/main`, puis repousser.
- Le hook Stop (`.claude/hooks/auto-push.sh`) applique cette règle automatiquement en fin de tour.

# Codex worker loop (paste this as the task prompt; run as many copies in parallel as you like)

You are one of several parallel workers on the CCP Avalonia port. Work items are briefs on branch
`avalonia-port/codex-briefs` in `docs/codex-briefs/`. `QUEUE.txt` lists them in priority order (one slug per line).
A brief is TAKEN when branch `avalonia-port/cloud-<slug>` exists on origin. Repeat until no brief is free:

1. Refresh: `git fetch origin avalonia-port/codex-briefs '+refs/heads/avalonia-port/cloud-*:refs/remotes/origin/avalonia-port/cloud-*'`
   and read `git show origin/avalonia-port/codex-briefs:docs/codex-briefs/QUEUE.txt`.
2. Pick the first slug with no `origin/avalonia-port/cloud-<slug>`. Read its brief:
   `git show origin/avalonia-port/codex-briefs:docs/codex-briefs/<slug>.md`. The brief names its BASE branch.
3. CLAIM it atomically before any work:
   `git fetch origin avalonia-port/<BASE> && git checkout -B avalonia-port/cloud-<slug> origin/avalonia-port/<BASE>`
   `git commit --allow-empty -m "claim: <slug> (codex)" && git push origin avalonia-port/cloud-<slug>`
   If the push is rejected (someone else claimed it), go back to step 1 and pick the next slug.
4. Do the brief exactly. Commit, then push the same branch (`git push origin avalonia-port/cloud-<slug>`, fast-forward only).
   Never force-push, never touch any other branch, never open a PR to `main`, never merge. Do not copy docs/codex-briefs into your branch.
5. If you cannot finish a brief, push what builds with a docs/cloud/<slug>.md note saying exactly what is left, then move on.
6. Go to step 1. Stop when every slug in QUEUE.txt has a branch, or when your session is about to end
   (never claim a brief you cannot finish in this session).

If this environment cannot push to origin at all: do ONE brief (the slug you were given, or the first in QUEUE.txt),
and use "Create PR" as a DRAFT with base `avalonia-port/<BASE>` from the brief; never base on main.

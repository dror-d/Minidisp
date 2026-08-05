# Minidisp Web Flasher

Static site (esp-web-tools) that flashes Minidisp firmware from the browser —
same approach as bruce.computer/flasher.

## Stage firmware

```
python firmware/scripts/release.py cyd     # builds + copies bins + manifest here
```

## Test locally

Web Serial requires HTTPS **or localhost**, so:

```
cd flasher
python -m http.server 8000
```

Open http://localhost:8000 in Chrome/Edge, connect the device, click Install.

## Publish

Deployment is automated: `.github/workflows/deploy-flasher.yml` builds all
firmware envs and deploys this folder to GitHub Pages on every push to main.
Live at: https://dror-d.github.io/Minidisp/

If the first run fails with a Pages error, enable it once in the repo:
Settings → Pages → Source: "GitHub Actions", then re-run the workflow.

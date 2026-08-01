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

Push this folder to a GitHub Pages branch (HTTPS is automatic). Keep the
`firmware/` binaries on the same origin to avoid CORS issues.

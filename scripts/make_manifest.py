#!/usr/bin/env python3
"""Writes integrity.json (SHA-256 + size of every file in a folder) and integrity.sig
(RSA PKCS#1 v1.5 / SHA-256 signature of integrity.json made with the code-signing key)."""
import hashlib, json, os, subprocess, sys, base64, datetime

folder, version, key, pwfile = sys.argv[1:5]
allowed_extra = ["Uninstall.exe", "terms.txt", "integrity.json", "integrity.sig", "portable.txt", "README.txt",
                 "Student Timer Clock Root CA.cer"]
files = []
for root, _, names in os.walk(folder):
    for n in sorted(names):
        p = os.path.join(root, n)
        rel = os.path.relpath(p, folder).replace("/", "\\")
        if rel in ("integrity.json", "integrity.sig"):
            continue
        h = hashlib.sha256(open(p, "rb").read()).hexdigest().upper()
        files.append({"Path": rel, "Size": os.path.getsize(p), "Sha256": h})
manifest = {
    "Product": "Student Timer Clock",
    "Version": version,
    "Created": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "Files": files,
    "AllowedExtraFiles": allowed_extra,
}
data = json.dumps(manifest, indent=2).encode()
open(os.path.join(folder, "integrity.json"), "wb").write(data)
sig = subprocess.run(["openssl", "dgst", "-sha256", "-sign", key, "-passin", "file:" + pwfile],
                     input=data, capture_output=True, check=True).stdout
open(os.path.join(folder, "integrity.sig"), "w").write(base64.b64encode(sig).decode())
for f in files:
    print(f"  {f['Sha256']}  {f['Size']:>10}  {f['Path']}")

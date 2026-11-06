#!/usr/bin/env python3
"""Generates the formal release record ("Certificate of Integrity") for a Student Timer Clock release."""
import argparse, datetime, hashlib, json, os, re, subprocess
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa, ec
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (BaseDocTemplate, Frame, KeepTogether, PageTemplate, Paragraph, Spacer, Table, TableStyle)

ap = argparse.ArgumentParser()
ap.add_argument("--root"); ap.add_argument("--work"); ap.add_argument("--version"); ap.add_argument("--pin"); ap.add_argument("--out")
a = ap.parse_args()
ROOT, WORK, VER = a.root, a.work, a.version
NAME = f"StudentTimerClock version {VER}"

NAVY = colors.HexColor("#14284B"); ACCENT = colors.HexColor("#0078D4"); GREY = colors.HexColor("#5F6670")
LIGHT = colors.HexColor("#EEF3F9"); LINE = colors.HexColor("#C9D3E0"); GREEN = colors.HexColor("#1E7B45"); AMBER = colors.HexColor("#8A5A00")

def sha256(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest().upper()

def sha1(p): return hashlib.sha1(open(p, "rb").read()).hexdigest().upper()

def grp(h, n=8): return " ".join(h[i:i + n] for i in range(0, len(h), n))

def size(p):
    s = os.path.getsize(p)
    return f"{s:,} bytes ({s / 1048576:.2f} MB)" if s > 1048576 else f"{s:,} bytes"

def verify_info(role):
    """osslsigncode verify output (written by build-release.sh) -> dict"""
    txt = open(os.path.join(WORK, f"verify-{role}.txt")).read()
    g = lambda rx: (re.search(rx, txt, re.M) or [None, ""])[1].strip()
    return {
        "digest": g(r"^Calculated message digest\s*:\s*([0-9A-F]+)"),
        "alg": g(r"^Message digest algorithm\s*:\s*(\S+)"),
        "signing_time": g(r"Authenticated attributes:\s*\n\s*Signing time:\s*(.+)"),
        "ts_time": g(r"Timestamp time:\s*(.+)"),
        "tsa": g(r"Countersignatures:[\s\S]*?Issuer:\s*(.+)"),
        "ok": "Succeeded" in txt and "Signature verification: ok" in txt,
    }

def cert_info(path):
    raw = open(path, "rb").read()
    c = x509.load_pem_x509_certificate(raw[raw.index(b"-----BEGIN CERTIFICATE"):]) if b"-----BEGIN CERTIFICATE" in raw else x509.load_der_x509_certificate(raw)
    pk = c.public_key()
    if isinstance(pk, rsa.RSAPublicKey): key = f"RSA {pk.key_size}-bit"
    elif isinstance(pk, ec.EllipticCurvePublicKey): key = f"ECDSA {pk.curve.name}"
    else: key = type(pk).__name__
    def ext(cls):
        try: return c.extensions.get_extension_for_class(cls).value
        except x509.ExtensionNotFound: return None
    ku = ext(x509.KeyUsage); eku = ext(x509.ExtendedKeyUsage); bc = ext(x509.BasicConstraints)
    kus = []
    if ku:
        for n in ["digital_signature", "content_commitment", "key_encipherment", "data_encipherment", "key_agreement", "key_cert_sign", "crl_sign"]:
            if getattr(ku, n): kus.append(n.replace("_", " ").title())
    ekus = [{"1.3.6.1.5.5.7.3.3": "Code Signing (1.3.6.1.5.5.7.3.3)"}.get(o.dotted_string, o._name if hasattr(o, "_name") else o.dotted_string) for o in eku] if eku else []
    der = c.public_bytes(serialization.Encoding.DER)
    return {
        "subject": c.subject.rfc4514_string(), "issuer": c.issuer.rfc4514_string(),
        "serial": format(c.serial_number, "X"),
        "from": c.not_valid_before_utc.strftime("%d %B %Y, %H:%M:%S UTC"), "to": c.not_valid_after_utc.strftime("%d %B %Y, %H:%M:%S UTC"),
        "key": key, "sigalg": c.signature_hash_algorithm.name.upper() + " with " + ("RSA" if "RSA" in key else "ECDSA"),
        "ku": ", ".join(kus) or "-", "eku": ", ".join(ekus) or "-",
        "bc": ("CA, path length " + str(bc.path_length)) if bc and bc.ca else "End entity (not a CA)",
        "sha1": hashlib.sha1(der).hexdigest().upper(), "sha256": hashlib.sha256(der).hexdigest().upper(),
        "self_signed": c.subject == c.issuer,
    }

# ---------------------------------------------------------------- data
inst_app = os.path.join(WORK, "stage-installer", "StudentTimerClock.exe")
port_app = os.path.join(WORK, "stage-portable", "StudentTimerClock.exe")
installer = os.path.join(WORK, f"{NAME}.exe")
portable_zip = os.path.join(WORK, f"{NAME} (portable, needs NET 8).zip")
manifest = os.path.join(WORK, "stage-installer", "integrity.json")
signer = cert_info(os.path.join(WORK, "certs", "signer.pem"))
root = cert_info(os.path.join(ROOT, "signing", "root-ca.crt"))
vi_inst, vi_app, vi_port = verify_info("installer"), verify_info("installed-app"), verify_info("portable-app")
assert vi_app["digest"] != vi_port["digest"], "verification logs mixed up"
H_APP = sha256(inst_app)
now = datetime.datetime.now(datetime.timezone.utc)
record_no = f"STC-REL-{VER}-{now:%Y%m%d}-{H_APP[:8]}"
assert signer["sha256"] == a.pin.upper(), "pinned certificate differs from signing certificate"

# ---------------------------------------------------------------- styles
ss = getSampleStyleSheet()
S = {
    "title": ParagraphStyle("t", parent=ss["Title"], fontName="Helvetica-Bold", fontSize=21, leading=25, textColor=NAVY, spaceAfter=2),
    "sub": ParagraphStyle("s", fontName="Helvetica", fontSize=10.5, leading=14, textColor=GREY, alignment=TA_CENTER),
    "h": ParagraphStyle("h", fontName="Helvetica-Bold", fontSize=11.5, leading=15, textColor=NAVY, spaceBefore=10, spaceAfter=5, keepWithNext=1),
    "p": ParagraphStyle("p", fontName="Helvetica", fontSize=8.8, leading=12, textColor=colors.black),
    "small": ParagraphStyle("sm", fontName="Helvetica", fontSize=7.6, leading=10, textColor=GREY),
    "k": ParagraphStyle("k", fontName="Helvetica-Bold", fontSize=8.2, leading=11, textColor=NAVY),
    "v": ParagraphStyle("v", fontName="Helvetica", fontSize=8.2, leading=11),
    "mono": ParagraphStyle("m", fontName="Courier", fontSize=7.9, leading=10.5),
    "monob": ParagraphStyle("mb", fontName="Courier-Bold", fontSize=8.4, leading=11),
    "code": ParagraphStyle("c", fontName="Courier", fontSize=7.6, leading=10, backColor=LIGHT, borderPadding=(4, 5, 4, 5), leftIndent=4, rightIndent=4, spaceBefore=3, spaceAfter=9),
}
P = lambda t, s="p": Paragraph(t, S[s])

def kv(rows, col=42 * mm):
    data = [[P(k, "k"), v if not isinstance(v, str) else P(v, "v")] for k, v in rows]
    t = Table(data, colWidths=[col, 174 * mm - col])
    t.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "TOP"), ("LINEBELOW", (0, 0), (-1, -2), 0.3, LINE),
                           ("BOTTOMPADDING", (0, 0), (-1, -1), 3.2), ("TOPPADDING", (0, 0), (-1, -1), 3.2), ("LEFTPADDING", (0, 0), (-1, -1), 4)]))
    return t

def box(flow, bg=LIGHT, border=LINE):
    t = Table([[flow]], colWidths=[174 * mm])
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), bg), ("BOX", (0, 0), (-1, -1), 0.6, border),
                           ("LEFTPADDING", (0, 0), (-1, -1), 7), ("RIGHTPADDING", (0, 0), (-1, -1), 7), ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6)]))
    return t

def page(c, doc):
    c.saveState()
    w, h = A4
    c.setFillColor(NAVY); c.rect(0, h - 14 * mm, w, 14 * mm, fill=1, stroke=0)
    c.setFillColor(colors.white); c.setFont("Helvetica-Bold", 10); c.drawString(18 * mm, h - 9 * mm, "STUDENT TIMER CLOCK")
    c.setFont("Helvetica", 8.5); c.drawRightString(w - 18 * mm, h - 9 * mm, f"Release Integrity Record  ·  {record_no}")
    c.setStrokeColor(LINE); c.line(18 * mm, 14 * mm, w - 18 * mm, 14 * mm)
    c.setFillColor(GREY); c.setFont("Helvetica", 7.3)
    c.drawString(18 * mm, 9.5 * mm, f"Version {VER}  ·  Issued {now:%d %B %Y %H:%M} UTC  ·  Generated by the release build pipeline (scripts/build-release.sh)")
    c.drawRightString(w - 18 * mm, 9.5 * mm, f"Page {doc.page}")
    c.restoreState()

doc = BaseDocTemplate(a.out, pagesize=A4, leftMargin=18 * mm, rightMargin=18 * mm, topMargin=22 * mm, bottomMargin=20 * mm,
                      title=f"Student Timer Clock {VER} - Certificate of Integrity", author="Student Timer Clock Release Engineering",
                      subject=f"Release integrity record {record_no}", creator="scripts/make_integrity_record.py",
                      keywords=f"Student Timer Clock, {VER}, SHA-256, Authenticode, {H_APP}")
doc.addPageTemplates([PageTemplate(id="p", frames=[Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="f")], onPage=page)])
st = []

# ---------------------------------------------------------------- cover block
st += [Spacer(1, 4 * mm), P("Certificate of Integrity", "title"),
       P(f"Official release record for <b>Student Timer Clock version {VER}</b> (Windows x64)", "sub"), Spacer(1, 5 * mm)]
seal_rows = [
    ["Record number", record_no], ["Product", "Student Timer Clock"], ["Version", f"{VER}  (file version {VER}.0.0)"],
    ["Platform", "Microsoft Windows 10 / 11, x64  ·  .NET 8 (self-contained in the installer)"],
    ["Date of issue", now.strftime("%A, %d %B %Y, %H:%M:%S UTC")],
    ["Signature status", "VALID - all executables carry a valid SHA-256 Authenticode signature with RFC 3161 timestamp" if all(v["ok"] for v in (vi_inst, vi_app, vi_port)) else "ATTENTION - see section 3"],
]
st.append(kv([(k, v) for k, v in seal_rows], 38 * mm))
st.append(Spacer(1, 4 * mm))
st.append(box([P("<b>SHA-256 of the core executable</b> (StudentTimerClock.exe, as installed by the setup program)", "k"), Spacer(1, 2),
               P(grp(H_APP), "monob")], bg=colors.HexColor("#E8F4EC"), border=GREEN))

# ---------------------------------------------------------------- 1 artefacts
st.append(P("1.  Release artefacts and SHA-256 hashes", "h"))
st.append(P("The SHA-256 values below identify the exact, unmodified files of this release. Any change to a file - even a single bit - produces a completely different value.", "small"))
st.append(Spacer(1, 3))
arts = [
    ("Core executable (installed)", "StudentTimerClock.exe", inst_app, "Self-contained single-file build; contains all program code and the .NET runtime. Installed to %LOCALAPPDATA%\\Programs\\StudentTimerClock."),
    ("Installer", f"{NAME}.exe", installer, "NSIS setup program (installer and uninstaller are signed)."),
    ("Portable executable", "StudentTimerClock.exe (portable)", port_app, "Framework-dependent single-file build inside the portable ZIP. Note: the .NET launcher of framework-dependent apps loads hostfxr.dll from the program folder before the program's own integrity checks run - keep the portable folder write-protected; the installed edition is not affected."),
    ("Portable package", f"{NAME} (portable, needs NET 8).zip", portable_zip, "ZIP archive: portable executable + signed integrity manifest."),
    ("Integrity manifest", "integrity.json (installed)", manifest, "Signed list of SHA-256 hashes checked by the program at runtime (signature: integrity.sig)."),
    ("Root CA certificate", "Student Timer Clock Root CA.cer", os.path.join(ROOT, "signing", "Student Timer Clock Root CA.cer"), "Public certificate of the issuing CA."),
    ("Signing certificate", "Student Timer Clock Code Signing.cer", os.path.join(ROOT, "signing", "Student Timer Clock Code Signing.cer"), "Public code-signing certificate."),
]
for role, fname, path, note in arts:
    st.append(KeepTogether([kv([("Role", f"<b>{role}</b>"), ("File", fname), ("Size", size(path)), ("SHA-256", P(grp(sha256(path)), "mono")), ("Notes", P(note, "small"))], 30 * mm), Spacer(1, 4)]))

# ---------------------------------------------------------------- 2 certificate
st.append(P("2.  Code-signing certificate", "h"))
st.append(kv([
    ("Subject", signer["subject"]), ("Issuer", signer["issuer"]), ("Serial number", P(signer["serial"], "mono")),
    ("Valid from", signer["from"]), ("Valid until", signer["to"]), ("Public key", signer["key"]), ("Signature algorithm", signer["sigalg"]),
    ("Key usage", signer["ku"]), ("Extended key usage", signer["eku"]), ("Basic constraints", signer["bc"]),
    ("Thumbprint (SHA-1)", P(grp(signer["sha1"]), "mono")),
    ("Fingerprint (SHA-256)", P(grp(signer["sha256"]) + "<br/><font color='#1E7B45'><b>= certificate pinned inside the executable</b></font>", "mono")),
], 38 * mm))
st.append(Spacer(1, 5))
st.append(P("<b>Issuing certificate authority</b>", "k"))
st.append(kv([
    ("Subject", root["subject"]), ("Type", "Self-signed root certificate (private CA of the project)" if root["self_signed"] else root["issuer"]),
    ("Serial number", P(root["serial"], "mono")), ("Valid", f"{root['from']}  -  {root['to']}"), ("Public key", root["key"]),
    ("Thumbprint (SHA-1)", P(grp(root["sha1"]), "mono")), ("Fingerprint (SHA-256)", P(grp(root["sha256"]), "mono")),
], 38 * mm))

# ---------------------------------------------------------------- 3 signatures
sig_head = [P("3.  Digital signatures (Authenticode)", "h")]
for label, path, vi in [("Core executable (installed)", inst_app, vi_app), ("Installer", installer, vi_inst), ("Portable executable", port_app, vi_port)]:
    st.append(KeepTogether(sig_head + [kv([
        ("File", f"<b>{label}</b>"),
        ("Result", f"<font color='#1E7B45'><b>Signature valid</b></font> - digest recomputed and matched; CMS signature verified against the certificate chain" if vi["ok"] else "<font color='red'><b>NOT VERIFIED</b></font>"),
        ("Digest algorithm", vi["alg"] or "SHA256"), ("Signed image digest", P(grp(vi["digest"]), "mono")),
        ("Signing time", vi["signing_time"] or "-"),
        ("Timestamp (RFC 3161)", f"{vi['ts_time']}  ·  {vi['tsa']}" if vi["ts_time"] else "none"),
    ], 38 * mm), Spacer(1, 4)])); sig_head = []
st.append(P("The signed image digest is the Authenticode hash defined by Microsoft (the whole file except the checksum, certificate-table entry and signature). "
            "The trusted timestamp proves the signature was made while the certificate was valid, so it stays valid after the certificate expires.", "small"))

# ---------------------------------------------------------------- 4 runtime protection
st.append(P("4.  Built-in tamper protection", "h"))
prot = [
    ("At every start, before any window opens", "Self-verification of the running executable: Authenticode digest recomputed, CMS signature verified, signer certificate compared with the pinned SHA-256 fingerprint above; full-file SHA-256 compared with the signed integrity manifest; Windows WinVerifyTrust verdict."),
    ("Every 5 minutes", "The same signature and hash checks are repeated; the program folder is scanned for files that are not in the signed manifest (DLL planting / side-loading)."),
    ("Every 10 seconds", "Loaded-module scan (every DLL must come from Windows / Program Files, be listed in the signed manifest, or carry a valid trusted signature); debugger detection; .NET profiler / startup-hook injection detection."),
    ("Process hardening", "Current directory removed from the DLL search path; DLL search restricted to System32 and the program folder; AppInit/global-hook/LSP extension DLLs blocked; DLLs from network shares and low-integrity locations blocked; .NET startup hooks disabled at build time."),
    ("On any violation", "The event is written to security.log, all functionality is frozen, the user is informed and the process is terminated. User data is never modified or deleted."),
    ("Input validation", "Strict allow-list validation of every value that reaches a process start, shell, mailto:, keystroke or file sink; CSV formula-injection protection; size, depth and zip-bomb limits for data and backups; actions from backups or data files changed outside the app are quarantined until confirmed by the user; data file sealed with a DPAPI-protected HMAC-SHA256."),
]
st.append(kv(prot, 44 * mm))

# ---------------------------------------------------------------- 5 how to verify
st.append(KeepTogether([P("5.  How to verify this release yourself", "h"), P("Windows PowerShell - compare the result with section 1:", "p"), P(f'Get-FileHash "$env:LOCALAPPDATA\\Programs\\StudentTimerClock\\StudentTimerClock.exe" -Algorithm SHA256<br/>'
            f'Get-FileHash ".\\{NAME}.exe" -Algorithm SHA256<br/>'
            f'Get-AuthenticodeSignature ".\\{NAME}.exe" | Format-List Status, SignerCertificate, TimeStamperCertificate', "code")]))
st.append(KeepTogether([P("Command prompt:", "p"), P(f'certutil -hashfile "{NAME}.exe" SHA256<br/>signtool verify /pa /v "{NAME}.exe"     (Windows SDK)', "code")]))
st.append(P("Explorer: right-click the file → Properties → <b>Digital Signatures</b> → Details → View Certificate; "
            "the certificate thumbprint must equal the SHA-1 thumbprint in section 2. "
            "The file <b>SHA256SUMS.txt</b> in the release folder carries a detached CMS signature (SHA256SUMS.txt.p7s) made with the same key; "
            "this document is accompanied by a detached signature as well (<i>…Certificate of Integrity.pdf.p7s</i>).", "p"))

# ---------------------------------------------------------------- 6 notice
st.append(P("6.  Important notice on trust", "h"))
st.append(box([P(
    "<b>The code-signing certificate was issued by the project's own private certificate authority (self-signed root), not by a publicly trusted "
    "commercial CA.</b> The signatures are cryptographically valid and prove that the files are unchanged since they were signed by the holder of the "
    "project's private key. However, Windows does not know this CA: Explorer and PowerShell report the root as <i>not trusted</i>, and SmartScreen may still "
    "show <i>Unknown publisher</i>. An organisation can establish trust by deploying <i>Student Timer Clock Root CA.cer</i> to the "
    "<i>Trusted Root Certification Authorities</i> and <i>Trusted Publishers</i> stores (e.g. via Group Policy). For public distribution, re-sign with an "
    "OV/EV certificate from a public CA - the build pipeline accepts any PFX (SIGN_PFX) and re-pins the certificate automatically.<br/><br/>"
    "This record is generated automatically by the release build pipeline from the actual release files. It is a technical attestation of file identity "
    "and signature status; it is not a third-party security audit, a legal certification, or a guarantee that the software is free of defects. "
    "Client-side tamper protection raises the effort needed to modify the program considerably, but no software running on a computer controlled by an "
    "attacker with administrative rights can be made impossible to modify.", "small")], bg=colors.HexColor("#FFF6E5"), border=colors.HexColor("#E0B252")))

# ---------------------------------------------------------------- attestation
st.append(Spacer(1, 6 * mm))
att = Table([[P("<b>Attestation</b><br/>The files listed in section 1 were built from source, digitally signed and verified by the "
                f"Student Timer Clock release pipeline on {now:%d %B %Y at %H:%M} UTC. The values in this record were computed from those files.", "p"),
              P(f"<b>Signed by</b><br/>Student Timer Clock Code Signing<br/><font face='Courier' size='7'>{grp(signer['sha1'], 4)}</font><br/>"
                f"<font size='7' color='#5F6670'>Record {record_no}</font>", "p")]], colWidths=[110 * mm, 64 * mm])
att.setStyle(TableStyle([("BOX", (0, 0), (-1, -1), 0.8, NAVY), ("LINEBEFORE", (1, 0), (1, 0), 0.5, LINE), ("VALIGN", (0, 0), (-1, -1), "TOP"),
                         ("LEFTPADDING", (0, 0), (-1, -1), 7), ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 7)]))
st.append(KeepTogether(att))

doc.build(st)

# machine-readable copy of the record
rec = {"record": record_no, "product": "Student Timer Clock", "version": VER, "issued_utc": now.isoformat(),
       "core_executable_sha256": H_APP,
       "files": {fname: {"sha256": sha256(p), "size": os.path.getsize(p)} for _, fname, p, _ in arts},
       "signing_certificate": signer, "root_ca": root,
       "signatures": {"core": vi_app, "installer": vi_inst, "portable": vi_port}}
json.dump(rec, open(os.path.splitext(a.out)[0] + ".json", "w"), indent=2)
print("   record :", record_no)
print("   PDF    :", a.out)

# -*- coding: utf-8 -*-
"""Generate demo files for WinPreviewPlus (all preview types)."""
from __future__ import annotations

import csv
import json
import math
import struct
import wave
import zipfile
from pathlib import Path

from docx import Document
from docx.shared import Pt, RGBColor
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill
from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parent


def ensure(p: Path) -> Path:
    p.mkdir(parents=True, exist_ok=True)
    return p


def write_text():
    d = ensure(ROOT / "01-text")
    (d / "readme.txt").write_text(
        "WinPreviewPlus — демо текстового превью\n"
        "=====================================\n\n"
        "Ctrl+F — поиск по тексту.\n"
        "Строка для поиска: ПРОБНИК-НАЙТИ-123\n\n"
        + "\n".join(f"строка {i}: пример содержимого превью" for i in range(1, 41)),
        encoding="utf-8",
    )
    (d / "notes.md").write_text(
        "# Markdown demo\n\n"
        "## Возможности\n\n"
        "- превью `.md`\n"
        "- поиск **ПРОБНИК-НАЙТИ-123**\n\n"
        "```csharp\nConsole.WriteLine(\"WinPreviewPlus\");\n```\n",
        encoding="utf-8",
    )
    (d / "data.json").write_text(
        json.dumps(
            {
                "product": "WinPreviewPlus",
                "find": "ПРОБНИК-НАЙТИ-123",
                "formats": ["txt", "md", "json", "csv", "xml"],
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    with (d / "table.csv").open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["id", "name", "note"])
        for i in range(1, 16):
            w.writerow([i, f"item-{i}", "ПРОБНИК-НАЙТИ-123" if i == 7 else "ok"])
    (d / "config.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        "<config>\n"
        "  <app>WinPreviewPlus</app>\n"
        "  <find>ПРОБНИК-НАЙТИ-123</find>\n"
        "</config>\n",
        encoding="utf-8",
    )
    (d / "script.ps1").write_text(
        "# Demo PowerShell\nWrite-Host 'WinPreviewPlus'\n# ПРОБНИК-НАЙТИ-123\n",
        encoding="utf-8",
    )


def write_office():
    d = ensure(ROOT / "02-office")
    doc = Document()
    doc.add_heading("WinPreviewPlus — Word", level=1)
    doc.add_paragraph(
        "Документ для Alt+P. Поиск Ctrl+F: ПРОБНИК-НАЙТИ-123"
    )
    p = doc.add_paragraph("Обычный абзац с кириллицей и латиницей — preview handler.")
    run = p.add_run(" Выделение.")
    run.bold = True
    run.font.color.rgb = RGBColor(0x00, 0x67, 0xC0)
    for i in range(1, 12):
        doc.add_paragraph(f"Пункт {i}. Содержимое для прокрутки в превью.")
    doc.save(d / "demo.docx")

    wb = Workbook()
    ws = wb.active
    ws.title = "Demo"
    ws["A1"] = "Product"
    ws["B1"] = "Find"
    ws["C1"] = "Value"
    ws["A1"].font = Font(bold=True, color="FFFFFF")
    ws["B1"].font = Font(bold=True, color="FFFFFF")
    ws["C1"].font = Font(bold=True, color="FFFFFF")
    fill = PatternFill("solid", fgColor="0067C0")
    for col in ("A", "B", "C"):
        ws[f"{col}1"].fill = fill
    ws["A2"] = "WinPreviewPlus"
    ws["B2"] = "ПРОБНИК-НАЙТИ-123"
    ws["C2"] = 42
    for i in range(3, 25):
        ws[f"A{i}"] = f"row-{i}"
        ws[f"B{i}"] = "sample"
        ws[f"C{i}"] = i * 10
    ws.column_dimensions["A"].width = 18
    ws.column_dimensions["B"].width = 22
    wb.save(d / "demo.xlsx")

    # Legacy BIFF .xls (optional — openpyxl cannot write this)
    try:
        import xlwt

        book = xlwt.Workbook()
        sheet = book.add_sheet("Demo")
        sheet.write(0, 0, "Product")
        sheet.write(0, 1, "Find")
        sheet.write(0, 2, "Value")
        sheet.write(1, 0, "WinPreviewPlus")
        sheet.write(1, 1, "ПРОБНИК-НАЙТИ-123")
        sheet.write(1, 2, 42)
        for i in range(2, 24):
            sheet.write(i, 0, f"row-{i + 1}")
            sheet.write(i, 1, "sample")
            sheet.write(i, 2, (i + 1) * 10)
        book.save(str(d / "demo.xls"))
    except ImportError:
        print("  skip demo.xls (optional: pip install xlwt)")

    # Minimal pptx via zip/xml (no python-pptx dependency)
    pptx = d / "demo.pptx"
    content_types = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
</Types>"""
    rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
</Relationships>"""
    pres = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:sldIdLst><p:sldId id="256" r:id="rId1"/></p:sldIdLst>
  <p:sldSz cx="9144000" cy="6858000"/>
</p:presentation>"""
    pres_rels = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
</Relationships>"""
    slide = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
    <p:spTree>
      <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
      <p:grpSpPr/>
      <p:sp>
        <p:nvSpPr><p:cNvPr id="2" name="Title"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
        <p:spPr>
          <a:xfrm><a:off x="457200" y="1143000"/><a:ext cx="8229600" cy="1143000"/></a:xfrm>
        </p:spPr>
        <p:txBody>
          <a:bodyPr/><a:lstStyle/>
          <a:p><a:r><a:rPr lang="ru-RU" sz="3200" b="1"/><a:t>WinPreviewPlus — PPTX</a:t></a:r></a:p>
          <a:p><a:r><a:rPr lang="ru-RU" sz="1800"/><a:t>Поиск: ПРОБНИК-НАЙТИ-123</a:t></a:r></a:p>
        </p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
</p:sld>"""
    with zipfile.ZipFile(pptx, "w", compression=zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", content_types)
        z.writestr("_rels/.rels", rels)
        z.writestr("ppt/presentation.xml", pres)
        z.writestr("ppt/_rels/presentation.xml.rels", pres_rels)
        z.writestr("ppt/slides/slide1.xml", slide)


def write_pdf():
    d = ensure(ROOT / "03-pdf")
    path = d / "demo.pdf"
    c = canvas.Canvas(str(path), pagesize=A4)
    w, h = A4
    c.setFont("Helvetica-Bold", 18)
    c.drawString(72, h - 72, "WinPreviewPlus — PDF demo")
    c.setFont("Helvetica", 12)
    c.drawString(72, h - 110, "Find (Ctrl+F): PROBNIK-NAYTI-123")
    y = h - 150
    for i in range(1, 28):
        c.drawString(72, y, f"Page line {i}: sample text for scroll and highlight.")
        y -= 18
        if y < 72:
            c.showPage()
            c.setFont("Helvetica", 12)
            y = h - 72
    c.showPage()
    c.setFont("Helvetica-Bold", 16)
    c.drawString(72, h - 72, "Page 2 — more content")
    c.setFont("Helvetica", 12)
    c.drawString(72, h - 110, "Second hit area: PROBNIK-NAYTI-123")
    c.save()


def write_archive():
    d = ensure(ROOT / "04-archive")
    payload = ensure(d / "_payload")
    (payload / "inside.txt").write_text(
        "Файл внутри архива\nПРОБНИК-НАЙТИ-123\n", encoding="utf-8"
    )
    (payload / "nested.json").write_text('{"ok": true}\n', encoding="utf-8")
    zip_path = d / "demo.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for f in payload.rglob("*"):
            if f.is_file():
                z.write(f, arcname=f.relative_to(payload).as_posix())


def write_images():
    d = ensure(ROOT / "05-images")
    # PNG photo-like
    img = Image.new("RGB", (1280, 720), (28, 32, 40))
    draw = ImageDraw.Draw(img)
    for i in range(0, 1280, 40):
        draw.line([(i, 0), (i, 720)], fill=(40, 48, 60))
    draw.rectangle([80, 80, 1200, 640], outline=(0, 103, 192), width=6)
    draw.text((120, 160), "WinPreviewPlus", fill=(255, 255, 255))
    draw.text((120, 230), "Image preview demo", fill=(180, 190, 210))
    draw.text((120, 300), "Alt+P  ·  scroll / zoom", fill=(0, 180, 255))
    img.save(d / "photo.png")
    img.convert("RGB").save(d / "photo.jpg", quality=90)

    # SVG
    (d / "logo.svg").write_text(
        """<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" width="800" height="500" viewBox="0 0 800 500">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#0a1628"/>
      <stop offset="100%" stop-color="#0067c0"/>
    </linearGradient>
  </defs>
  <rect width="800" height="500" fill="url(#g)"/>
  <circle cx="400" cy="220" r="90" fill="none" stroke="#7dd3fc" stroke-width="8"/>
  <text x="400" y="360" text-anchor="middle" fill="white"
        font-family="Segoe UI, Arial" font-size="42" font-weight="700">WinPreviewPlus</text>
  <text x="400" y="410" text-anchor="middle" fill="#bae6fd"
        font-family="Segoe UI, Arial" font-size="22">SVG preview (WebView2)</text>
</svg>
""",
        encoding="utf-8",
    )

    # GIF tiny animation frames as static multi-frame-ish single gif
    frames = []
    for i in range(8):
        fr = Image.new("RGB", (320, 200), (20, 24, 32))
        dr = ImageDraw.Draw(fr)
        dr.ellipse([40 + i * 20, 60, 120 + i * 20, 140], fill=(0, 103, 192))
        dr.text((20, 20), f"GIF {i+1}", fill=(255, 255, 255))
        frames.append(fr)
    frames[0].save(
        d / "anim.gif",
        save_all=True,
        append_images=frames[1:],
        duration=120,
        loop=0,
    )


def write_wav():
    d = ensure(ROOT / "06-audio")
    path = d / "tone.wav"
    rate = 44100
    duration = 3.0
    freq = 440.0
    amp = 0.3
    n = int(rate * duration)
    with wave.open(str(path), "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        frames = bytearray()
        for i in range(n):
            # mild fade envelope
            env = min(1.0, i / (rate * 0.05), (n - i) / (rate * 0.2))
            val = int(amp * env * 32767 * math.sin(2 * math.pi * freq * i / rate))
            frames += struct.pack("<h", val)
        w.writeframes(frames)


def _ffmpeg() -> str | None:
    import shutil

    found = shutil.which("ffmpeg")
    if found:
        return found
    # winget yt-dlp.FFmpeg layout
    base = Path.home() / "AppData/Local/Microsoft/WinGet/Packages"
    if base.exists():
        for p in base.rglob("ffmpeg.exe"):
            return str(p)
    return None


def write_media():
    """MP3 + MP4 via ffmpeg (optional if missing)."""
    import subprocess

    write_wav()
    ff = _ffmpeg()
    audio = ensure(ROOT / "06-audio")
    video = ensure(ROOT / "07-video")
    if not ff:
        print("WARN: ffmpeg not found — skip mp3/mp4")
        return
    subprocess.run(
        [
            ff,
            "-y",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:duration=3",
            "-c:a",
            "libmp3lame",
            "-q:a",
            "4",
            str(audio / "tone.mp3"),
        ],
        check=False,
        capture_output=True,
    )
    subprocess.run(
        [
            ff,
            "-y",
            "-f",
            "lavfi",
            "-i",
            "color=c=0x0a1628:s=1280x720:d=4:r=30",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=523:duration=4",
            "-vf",
            "drawtext=text='WinPreviewPlus video':fontsize=48:fontcolor=white:x=(w-text_w)/2:y=(h-text_h)/2",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-shortest",
            str(video / "demo.mp4"),
        ],
        check=False,
        capture_output=True,
    )


def main():
    write_text()
    write_office()
    write_pdf()
    write_archive()
    write_images()
    write_media()
    print("Samples generated under:", ROOT)


if __name__ == "__main__":
    main()

# Generates src/icsmoi.Shared.Ui/Assets/icsmoi.ico — the icsmoi monogram (dark disc, orange ring,
# light 'i' stem, orange dot) at 16/24/32/48/64/128/256 px as a PNG-compressed multi-size .ico.
#
# The primitives mirror the Icsmoi.Monogram theme in src/icsmoi.Shared.Ui/Styles/Logo.axaml
# (64 x 64 grid). Keep the two in sync. Colors mirror Tokens.axaml (Background/Text/Accent).
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools/GenerateAppIcon.ps1

Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot '..\src\icsmoi.Shared.Ui\Assets\icsmoi.ico'
$sizes   = 16, 24, 32, 48, 64, 128, 256

$disc   = [System.Drawing.ColorTranslator]::FromHtml('#101216')
$orange = [System.Drawing.ColorTranslator]::FromHtml('#FF5A1F')
$light  = [System.Drawing.ColorTranslator]::FromHtml('#F4F5F7')

function New-MonogramPng([int]$px) {
    $s   = $px / 64.0
    $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode   = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # disc + ring (ring centreline inset 2 units, stroke 4 -> outer edge flush with the canvas)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $disc), [single](2 * $s), [single](2 * $s), [single](60 * $s), [single](60 * $s))
    $ring = New-Object System.Drawing.Pen $orange, ([single](4 * $s))
    $g.DrawEllipse($ring, [single](2 * $s), [single](2 * $s), [single](60 * $s), [single](60 * $s))

    # 'i' stem: round-capped line, 9 units thick, y 30..45
    $stem = New-Object System.Drawing.Pen $light, ([single](9 * $s))
    $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
    $g.DrawLine($stem, [single](32 * $s), [single](30 * $s), [single](32 * $s), [single](45 * $s))

    # 'i' dot: 10 units across, centred at (32, 17.5)
    $d = 10 * $s
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $orange), [single](32 * $s - $d / 2), [single](17.5 * $s - $d / 2), [single]$d, [single]$d)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($px in $sizes) { , (New-MonogramPng $px) }

# ICONDIR (6 bytes) + one 16-byte ICONDIRENTRY per image, then the PNG payloads.
$dir = New-Object System.IO.MemoryStream
$w   = New-Object System.IO.BinaryWriter $dir
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $px  = $sizes[$i]
    $dim = if ($px -ge 256) { 0 } else { $px }   # 0 means 256 in the ICO format
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()

New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
[System.IO.File]::WriteAllBytes($outPath, $dir.ToArray())
Write-Host "Wrote $outPath ($($sizes -join ', ') px)"

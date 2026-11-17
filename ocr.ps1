param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$Languages = "en-US,ru"
)

$ErrorActionPreference = "Stop"

# Без этого кириллица уезжает в OEM-кодировку и приходит в программу мусором.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataWriter, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]

$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]

function Await($operation, $resultType) {
    $asTask = $asTaskGeneric.MakeGenericMethod($resultType)
    $netTask = $asTask.Invoke($null, @($operation))
    $netTask.Wait(-1) | Out-Null
    return $netTask.Result
}

try {
    $bytes = [System.IO.File]::ReadAllBytes($Path)

    $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    $writer = New-Object Windows.Storage.Streams.DataWriter($stream)
    $writer.WriteBytes($bytes)
    $null = Await ($writer.StoreAsync()) ([uint32])
    $null = $writer.FlushAsync()
    $stream.Seek(0)

    $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])

    $results = @{}

    foreach ($tag in $Languages.Split(',')) {
        $tag = $tag.Trim()
        if ($tag.Length -eq 0) { continue }

        try {
            $lang = New-Object Windows.Globalization.Language($tag)
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
            if ($null -eq $engine) { continue }

            $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])

            $lines = @()
            foreach ($line in $result.Lines) {
                $minX = [double]::MaxValue
                $minY = [double]::MaxValue
                $maxX = 0.0
                $maxY = 0.0

                foreach ($word in $line.Words) {
                    $r = $word.BoundingRect
                    if ($r.X -lt $minX) { $minX = $r.X }
                    if ($r.Y -lt $minY) { $minY = $r.Y }
                    if (($r.X + $r.Width) -gt $maxX) { $maxX = $r.X + $r.Width }
                    if (($r.Y + $r.Height) -gt $maxY) { $maxY = $r.Y + $r.Height }
                }

                if ($minX -eq [double]::MaxValue) { continue }

                $clean = $line.Text -replace '[\x00-\x1F\x7F]', ''

                $lines += [pscustomobject]@{
                    text = $clean
                    x    = [int]$minX
                    y    = [int]$minY
                    w    = [int]($maxX - $minX)
                    h    = [int]($maxY - $minY)
                }
            }

            $results[$tag] = $lines
        }
        catch {
            $results[$tag] = @()
        }
    }

    $payload = [pscustomobject]@{
        width   = $decoder.PixelWidth
        height  = $decoder.PixelHeight
        results = $results
    }

    $json = $payload | ConvertTo-Json -Depth 8 -Compress
    $stream.Dispose()
    Write-Output $json
}
catch {
    $detail = $_.Exception.ToString()
    if ($_.Exception.InnerException) { $detail += " | " + $_.Exception.InnerException.ToString() }
    $detail = $detail.Replace('"', "'").Replace("`r", " ").Replace("`n", " ")
    Write-Output ('{"error":"' + $detail + '"}')
}

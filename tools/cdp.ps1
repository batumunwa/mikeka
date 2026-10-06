# Read-only helper: runs JavaScript in the system Chrome tab whose URL contains -UrlPart (CDP at localhost:9222).
# Usage: powershell -File tools/cdp.ps1 -UrlPart leonbet -Js "location.href"
param([string]$UrlPart, [string]$Js)
$tabs = Invoke-RestMethod http://localhost:9222/json
$tab = $tabs | Where-Object { $_.type -eq 'page' -and $_.url -like "*$UrlPart*" } | Select-Object -First 1
if (-not $tab) { "no tab"; exit }
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync([Uri]$tab.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
$msg = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $Js; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 5 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($msg)
$ws.SendAsync([ArraySegment[byte]]$bytes, 'Text', $true, [Threading.CancellationToken]::None).Wait()
$buf = New-Object byte[] 1048576; $sb = New-Object Text.StringBuilder
do { $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result; [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) } while (-not $r.EndOfMessage)
$ws.Dispose()
$sb.ToString()

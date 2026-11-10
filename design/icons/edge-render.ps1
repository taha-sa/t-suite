param([string]$Edge, [string]$Manifest)
# Edge's Windows CLI can subtract non-client chrome from the SVG viewport.
# CDP gives exact CSS-pixel viewports and native PNGs without cropping/resampling.
$ErrorActionPreference='Stop'
$jobs=Get-Content $Manifest -Raw | ConvertFrom-Json
$profile=Join-Path $env:TEMP ('tsuite-icons-cdp-'+[guid]::NewGuid().ToString('N'))
$portFile=Join-Path $profile 'DevToolsActivePort'
$browserProcess=$null; $socket=$null
$script:requestId=0
function Request($method,$parameters) {
    $script:requestId++
    $id=$script:requestId
    $json=@{id=$id;method=$method;params=$parameters} | ConvertTo-Json -Depth 20 -Compress
    $bytes=[Text.Encoding]::UTF8.GetBytes($json)
    $segment=New-Object 'System.ArraySegment[byte]' -ArgumentList (,$bytes)
    $socket.SendAsync($segment,[Net.WebSockets.WebSocketMessageType]::Text,$true,[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    while($true) {
        $memory=New-Object IO.MemoryStream
        try {
            do {
                $buffer=New-Object byte[] 65536
                $part=New-Object 'System.ArraySegment[byte]' -ArgumentList (,$buffer)
                $received=$socket.ReceiveAsync($part,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
                if($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close){throw 'Edge closed the screenshot connection.'}
                $memory.Write($buffer,0,$received.Count)
            } while(-not $received.EndOfMessage)
            $reply=[Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json
        } finally { $memory.Dispose() }
        if($reply.id -eq $id) {
            if($reply.error){throw ($reply.error | ConvertTo-Json -Compress)}
            return $reply.result
        }
    }
}
try {
    $args=@('--headless=new','--disable-gpu','--no-sandbox','--no-first-run','--disable-extensions','--disable-background-networking','--disable-background-mode','--disable-sync','--hide-scrollbars','--force-device-scale-factor=1','--remote-debugging-port=0','--window-size=1024,1024',"--user-data-dir=$profile",'about:blank')
    for($attempt=0;$attempt -lt 3;$attempt++) {
        $browserProcess=Start-Process $Edge -ArgumentList ($args | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
        $deadline=[DateTime]::UtcNow.AddSeconds(20)
        while(-not (Test-Path $portFile) -and -not $browserProcess.HasExited -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
        if(Test-Path $portFile){break}
        if(-not $browserProcess.HasExited){$browserProcess.Kill();$browserProcess.WaitForExit()}
    }
    if(-not (Test-Path $portFile)){throw 'Edge remote debugging failed to start.'}
    $port=(Get-Content $portFile)[0]
    $pages=Invoke-RestMethod "http://127.0.0.1:$port/json/list" -UseBasicParsing
    $page=$pages | Where-Object {$_.type -eq 'page'} | Select-Object -First 1
    $socket=New-Object Net.WebSockets.ClientWebSocket
    $socket.ConnectAsync([uri]$page.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    Request 'Page.enable' @{} | Out-Null
    Request 'Emulation.setDefaultBackgroundColorOverride' @{color=@{r=0;g=0;b=0;a=0}} | Out-Null
    foreach($job in $jobs) {
        $n=[int]$job.size; $uri=([uri]$job.html).AbsoluteUri
        Request 'Emulation.setDeviceMetricsOverride' @{width=$n;height=$n;deviceScaleFactor=1;mobile=$false} | Out-Null
        Request 'Page.navigate' @{url=$uri} | Out-Null
        $loaded=$false
        for($poll=0;$poll -lt 100;$poll++) {
            $state=Request 'Runtime.evaluate' @{expression='JSON.stringify({url:location.href,ready:document.readyState})';returnByValue=$true}
            if($state.result.value){
                $value=$state.result.value | ConvertFrom-Json
                if($value.url -eq $uri -and $value.ready -eq 'complete'){$loaded=$true;break}
            }
            Start-Sleep -Milliseconds 20
        }
        if(-not $loaded){throw "Page did not load: $uri"}
        Request 'Runtime.evaluate' @{expression='new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))';awaitPromise=$true} | Out-Null
        $capture=Request 'Page.captureScreenshot' @{format='png';captureBeyondViewport=$true;clip=@{x=0;y=0;width=$n;height=$n;scale=1}}
        [IO.File]::WriteAllBytes($job.png,[Convert]::FromBase64String($capture.data))
    }
} finally {
    if($socket){$socket.Dispose()}
    if($browserProcess -and -not $browserProcess.HasExited){$browserProcess.Kill();$browserProcess.WaitForExit()}
}
Write-Host "Edge captured $($jobs.Count) exact viewports."

param(
    [Parameter(Mandatory)]
    [int]$Port,
    [Parameter(Mandatory)]
    [string]$ReadyFile
)

$ErrorActionPreference = "Stop"
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Set-Content -LiteralPath $ReadyFile -Value "ready" -Encoding ascii
$version = 1
$resources = @{}

function Get-MultiStatus {
    param([string]$Inner)

    return @"
<?xml version="1.0" encoding="utf-8"?>
<d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
$Inner
</d:multistatus>
"@
}

function Get-ResourceResponses([int]$SinceVersion) {
    foreach ($resourcePath in ($resources.Keys | Sort-Object)) {
        $entry = $resources[$resourcePath]
        if ($entry.Version -le $SinceVersion) { continue }
        "<d:response><d:href>$resourcePath</d:href><d:propstat><d:prop><d:getetag>`"v$($entry.Version)`"</d:getetag><d:getcontenttype>text/calendar</d:getcontenttype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>"
    }
}

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        $path = $request.Url.AbsolutePath
        $method = $request.HttpMethod
        $body = ""
        $status = 207
        $contentType = 'application/xml; charset=utf-8'

        if ($method -eq "PROPFIND" -and $path -eq "/") {
            $body = Get-MultiStatus @"
  <d:response><d:href>/</d:href><d:propstat><d:prop>
    <d:current-user-principal><d:href>/principal/</d:href></d:current-user-principal>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
"@
        } elseif ($method -eq "PROPFIND" -and $path -eq "/principal/") {
            $body = Get-MultiStatus @"
  <d:response><d:href>/principal/</d:href><d:propstat><d:prop>
    <c:calendar-home-set><d:href>/cal/</d:href></c:calendar-home-set>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
"@
        } elseif ($method -eq "PROPFIND" -and $path -eq "/cal/") {
            $body = Get-MultiStatus @"
  <d:response><d:href>/cal/tasks/</d:href><d:propstat><d:prop>
    <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
    <d:displayname>Tasks</d:displayname><cs:getctag>ctag-$version</cs:getctag>
    <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
"@
        } elseif ($method -eq "PROPFIND" -and $path -eq "/cal/tasks/") {
            $entries = if ($request.Headers['Depth'] -eq '1') { (Get-ResourceResponses 0) -join "`n" }
            $body = Get-MultiStatus @"
  <d:response><d:href>/cal/tasks/</d:href><d:propstat><d:prop>
    <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
    <d:displayname>Tasks</d:displayname><cs:getctag>ctag-$version</cs:getctag><d:sync-token>token-$version</d:sync-token>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
$entries
"@
        } elseif ($method -eq "REPORT" -and $path -eq "/cal/tasks/") {
            $reader = [IO.StreamReader]::new($request.InputStream)
            try { $report = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $since = if ($report -match '>token-(\d+)<') { [int]$Matches[1] } else { 0 }
            $entries = (Get-ResourceResponses $since) -join "`n"
            $body = Get-MultiStatus "$entries<d:sync-token>token-$version</d:sync-token>"
        } elseif ($path -match '^/cal/tasks/[A-Za-z0-9_-]+\.ics$' -and $method -eq 'PUT') {
            $reader = [IO.StreamReader]::new($request.InputStream)
            try { $calendar = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $status = if ($resources.ContainsKey($path)) { 204 } else { 201 }
            $version++
            $resources[$path] = @{ Content = $calendar; Version = $version }
            $response.Headers['ETag'] = '"v' + $version + '"'
        } elseif ($method -eq 'GET' -and $resources.ContainsKey($path)) {
            $entry = $resources[$path]
            $body = $entry.Content
            $contentType = 'text/calendar; charset=utf-8'
            $response.Headers['ETag'] = '"v' + $entry.Version + '"'
            $status = 200
        } elseif ($method -eq "PROPPATCH") {
            $body = Get-MultiStatus ""
        } else {
            $status = 204
        }

        $response.StatusCode = $status
        if ($body) {
            $bytes = [Text.Encoding]::UTF8.GetBytes($body)
            $response.ContentType = $contentType
            $response.ContentLength64 = $bytes.Length
            $response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        $response.Close()
    }
} finally {
    $listener.Stop()
    $listener.Close()
}

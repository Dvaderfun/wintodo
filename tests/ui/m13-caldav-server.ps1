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

function Get-MultiStatus {
    param([string]$Inner)

    return @"
<?xml version="1.0" encoding="utf-8"?>
<d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
$Inner
</d:multistatus>
"@
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
    <d:displayname>Tasks</d:displayname><cs:getctag>ctag-1</cs:getctag>
    <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
"@
        } elseif ($method -eq "PROPFIND" -and $path -eq "/cal/tasks/") {
            $body = Get-MultiStatus @"
  <d:response><d:href>/cal/tasks/</d:href><d:propstat><d:prop>
    <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
    <d:displayname>Tasks</d:displayname><cs:getctag>ctag-1</cs:getctag><d:sync-token>token-1</d:sync-token>
  </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
"@
        } elseif ($method -eq "REPORT" -and $path -eq "/cal/tasks/") {
            $body = Get-MultiStatus "  <d:sync-token>token-1</d:sync-token>"
        } elseif ($method -eq "PROPPATCH") {
            $body = Get-MultiStatus ""
        } else {
            $status = 204
        }

        $response.StatusCode = $status
        if ($body) {
            $bytes = [Text.Encoding]::UTF8.GetBytes($body)
            $response.ContentType = "application/xml; charset=utf-8"
            $response.ContentLength64 = $bytes.Length
            $response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        $response.Close()
    }
} finally {
    $listener.Stop()
    $listener.Close()
}

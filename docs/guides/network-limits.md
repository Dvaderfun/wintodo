# Network response and import attachment limits

DEP-07 bounds response bytes before they reach parsers or attachment files.
The production Graph and CalDAV transports request headers first, disable
automatic redirects and do not enable automatic decompression. Responses with
an unhandled Content-Encoding are rejected before reading; compressed payloads
are not supported. The boundary counts bytes actually delivered by the HTTP
stream, including decompressed bytes if a custom handler already decoded them
and removed the encoding/length headers.

| Content | Maximum |
|---|---|
| CalDAV XML, including protocol error bodies | 16 MiB of response bytes |
| CalDAV calendar resource | 32 MiB of response bytes |
| Graph JSON page or account response | 16 MiB of response bytes |
| Graph terminal error response | 64 KiB of response bytes |
| Graph raw attachment | 25 MiB of actual file bytes |
| Graph attachment JSON fallback envelope | 40 MiB of response bytes |
| Decoded fallback attachment | 25 MiB of actual file bytes |
| Pending attachments in one import attempt | 256 MiB of actual decoded/file bytes |

MiB means 1,048,576 bytes. CalDAV retains its existing 32 MiB resource limit
and the XML parser's 16,777,216-character limit, DTD prohibition and disabled
external resolver. Its former 16 MiB declared-length XML check now applies to
actual bytes too, including unknown lengths and multibyte text. Graph's new
25 MiB file limit matches the existing local attachment picker limit. The
40 MiB fallback envelope allows base64 expansion and bounded metadata; decoded
output has its separate file cap. Invalid/trailing malformed JSON, duplicate
contentBytes fields and short bodies with declared lengths are rejected.

Declared lengths above a limit fail before body reads. Other bodies use a
forward-only bounded stream which reads at most one byte beyond its budget
to distinguish exact-limit EOF from overflow. Over-budget chunks are never
returned to a parser or written to a destination. Returned data must reach EOF
and match any declared Content-Length. Production HTTP framing also detects
truncated responses.

Each Graph JSON request and complete attachment retrieval, including its
fallback and retries, has a 100-second deadline. Each public CalDAV request
operation, including discovery/fallback requests and XML parsing, has the
same deadline. A shorter HttpClient timeout is retained as the deadline;
an infinite timeout cannot remove the 100-second bound. Caller cancellation
also applies throughout reads, parsing/decoding and writes. Graph pages have
individual deadlines; the import session can cancel the entire multi-page
read. The synchronous BCL streaming base64 parser runs on a background task,
and its read adapter uses cancellable network reads, so warm headers cannot
make it block the WinUI thread.

Staging validates declared attachment totals before creating files and enforces
the remaining aggregate budget against actual output. A failing/cancelled
attempt closes and deletes only the numbered file it created. Failure to create
a new file preserves the existing file. Earlier complete downloads, committed
imports and unrelated files are preserved. Destination IO errors, including
disk-full failures, stop further reads and use this same cleanup path; removal
can still fail if filesystem permissions or the filesystem itself deny it.
Wizard staging directories are unique per attempt and retain their existing
final cleanup. These limits do not change origin checks, GET-only Graph
requests, CalDAV conditional writes or fixture byte-preservation behavior.

Automated coverage lives in GraphNetworkLimitTests, GraphNetworkStreamTests,
CalDavNetworkLimitTests, CalDavNetworkStreamTests and BoundedReadStreamTests.
It uses synthetic content and isolated loopback servers, including the real
production handlers, chunked/unknown/truncated/slow responses, exact boundaries,
decoded output and aggregate budgets, cancellation and destination IO failure.
It does not require live accounts, private tasks, machine provisioning or host
UI automation.

# protos

gRPC contracts shared between services. Projects reference these files directly from their
`.csproj` (`<Protobuf Include="..\..\..\..\shared\protos\catalog.proto" />`), so there is one
source of truth and no copied files.

| File | Server | Clients |
|---|---|---|
| `catalog.proto` | `catalog` (port 8081, HTTP/2 cleartext) | `orders` |

Rules: never renumber or reuse a field number, add new fields instead of changing types, and
keep money as integer minor units (`price_cents`).
